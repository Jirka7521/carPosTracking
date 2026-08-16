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
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="logger">Structured logger that receives the exception.</param>
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
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
        // mid-map-refresh.
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        _logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Server error",
                Detail = "The server encountered an error. Please try again later.",
            },
            cancellationToken);

        return true;
    }
}
