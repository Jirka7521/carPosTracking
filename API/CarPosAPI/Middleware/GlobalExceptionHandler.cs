using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Middleware;

/// <summary>
/// The single place unhandled exceptions are turned into responses.
///
/// It logs the exception in full — that is what the server-side log is for — and
/// returns a <see cref="ProblemDetails"/> body that says nothing beyond "500".
/// The message, the stack trace and any SQL in it stay on this side of the wire:
/// an exception message is written for a developer reading a log, and routinely
/// contains connection strings, table names and parameter values.
///
/// Because this exists, no action needs its own try/catch. Expected failures never
/// reach here at all — services return
/// <see cref="Services.Common.OperationResult{T}"/> instead.
///
/// There is one case where the "always a clean 500" promise cannot be kept: a
/// response that has already begun streaming, which <c>GET /api/me/export</c> does.
/// Its status line and part of its body are on the wire and cannot be recalled.
/// ASP.NET Core handles that case before this class is reached — the exception
/// handler middleware checks <c>HasStarted</c> itself, logs the exception, and
/// rethrows so Kestrel aborts the connection, which is what makes the client report
/// a failed download rather than saving a truncated file. The guard in
/// <see cref="TryHandleAsync"/> is belt to that braces: unreachable through
/// <c>UseExceptionHandler</c>, correct if this handler is ever called directly.
///
/// The body is written through <see cref="IProblemDetailsService"/> rather than
/// serialised directly, so it carries the correct <c>application/problem+json</c>
/// content type and picks up the <c>traceId</c> that Program.cs stamps on every
/// problem response.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    /// <summary>
    /// What the client is told, whatever actually happened. Deliberately constant:
    /// a message that varied with the fault would let a caller probe the server by
    /// reading the differences.
    /// </summary>
    private const string OpaqueDetail = "The server encountered an error. Please try again later.";

    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetails;

    /// <summary>Creates the handler.</summary>
    /// <param name="logger">Structured logger that receives the exception.</param>
    /// <param name="problemDetails">Writes the ProblemDetails body in the negotiated format.</param>
    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IProblemDetailsService problemDetails)
    {
        _logger = logger;
        _problemDetails = problemDetails;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // A cancelled request is the client hanging up, not a fault. Logging it as
        // an error would fill the log with noise every time someone closes a tab
        // mid-map-refresh. RequestAborted is checked as well as the handler's own
        // token because the two are not always the same instance — a cancellation
        // raised deep in EF carries whichever token the call chain was given.
        if (exception is OperationCanceledException
            && (cancellationToken.IsCancellationRequested || httpContext.RequestAborted.IsCancellationRequested))
        {
            // Debug rather than nothing: a burst of these is how a proxy timeout or
            // a client-side abort loop shows up, and silence would hide it.
            _logger.LogDebug(
                "Client cancelled {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);

            return false;
        }

        // A response that has already begun streaming, which GET /api/me/export does.
        //
        // Not reached through UseExceptionHandler: that middleware tests HasStarted
        // before it calls any handler, logs the exception itself and rethrows, so
        // Kestrel aborts the connection. This branch exists so the class is correct
        // on its own terms rather than only in the pipeline it happens to sit in —
        // without it, a direct call would throw from inside an exception handler,
        // which is the worst place for a new exception to come from.
        //
        // Nothing can be salvaged either way: setting StatusCode on a started
        // response throws, and writing a ProblemDetails would append an error object
        // to a half-finished document, producing a file that is neither valid nor
        // obviously broken. Aborting is what makes the client report a failed
        // transfer instead of saving a truncated export as though it had succeeded.
        if (httpContext.Response.HasStarted)
        {
            _logger.LogError(
                exception,
                "Unhandled exception after the response had started for {Method} {Path} — aborting the connection so the client sees a failed transfer rather than a truncated body",
                httpContext.Request.Method,
                httpContext.Request.Path);

            httpContext.Abort();

            return true;
        }

        // Kestrel raises this for a request it could not read as HTTP at all: a body
        // over the configured size limit, a malformed chunked encoding, too many or
        // too-long headers. The caller broke the request, so answering 500 would be
        // both wrong and misleading — it would say the server failed and invite a
        // retry that is guaranteed to fail the same way. The exception carries the
        // status Kestrel had already decided on, so use it.
        //
        // Logged at Warning rather than Error for the same reason: this is not a
        // fault in this application, though a run of them is still worth seeing.
        if (exception is BadHttpRequestException badRequest)
        {
            _logger.LogWarning(
                exception,
                "Malformed request {Method} {Path} rejected with {StatusCode}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                badRequest.StatusCode);

            return await WriteProblemAsync(
                httpContext,
                badRequest.StatusCode,
                "Invalid request",
                DescribeClientFault(badRequest.StatusCode),
                cancellationToken);
        }

        _logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        return await WriteProblemAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "Server error",
            OpaqueDetail,
            cancellationToken);
    }

    /// <summary>
    /// The safe, caller-facing sentence for a request Kestrel refused to read.
    ///
    /// Written from scratch rather than taken from the exception message: that
    /// message is a developer-facing string that names the limit that was hit, and
    /// telling a caller exactly where a boundary sits is how they find the edges.
    /// </summary>
    /// <param name="statusCode">The status Kestrel chose for the fault.</param>
    /// <returns>A sentence safe to put on the wire.</returns>
    private static string DescribeClientFault(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status413PayloadTooLarge => "The request body is too large.",
            StatusCodes.Status431RequestHeaderFieldsTooLarge => "The request headers are too large.",
            _ => "The request could not be read.",
        };
    }

    /// <summary>
    /// Writes one ProblemDetails response, negotiated, with a direct fallback.
    /// </summary>
    /// <param name="httpContext">The request being answered.</param>
    /// <param name="statusCode">The status to return.</param>
    /// <param name="title">Short, generic summary.</param>
    /// <param name="detail">Caller-facing sentence — never anything internal.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Always true: the response has been handled either way.</returns>
    private async ValueTask<bool> WriteProblemAsync(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = statusCode;

        ProblemDetails problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
        };

        // The exception is deliberately NOT attached to the context. ProblemDetails
        // writers are free to read it, and this one must never be in a position to
        // put any part of it on the wire.
        bool written = await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });

        if (!written)
        {
            // TryWriteAsync declines when the caller's Accept header rules out every
            // format it can produce. A 500 with no body at all would be worse than
            // one in a format the client did not ask for, so fall back rather than
            // returning false and letting the exception escape to Kestrel unlogged.
            await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        }

        return true;
    }
}
