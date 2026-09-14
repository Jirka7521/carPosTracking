using System.Text.Json;
using CarPosAPI.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="GlobalExceptionHandler"/> — the last thing standing between an
/// exception and the public internet.
///
/// <para>
/// The redaction tests are the reason this class exists, and they are the sibling of
/// the ones in <see cref="HealthReportWriterTests"/>. An exception message is written
/// for a developer reading a log: an Npgsql failure names the host, the database and
/// the role, and a stack trace names every internal type on the path. Keeping all of
/// that off the wire is the handler's whole job, and nothing else in the system would
/// notice if it stopped doing it.
/// </para>
///
/// <para>
/// The rest cover the two cases where the handler has to do something other than
/// write a clean 500: a response that has already begun streaming, and a request
/// Kestrel refused to read.
/// </para>
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    /// <summary>
    /// Stands in for the kind of text a real exception carries. Every redaction
    /// assertion below looks for a piece of this in the response body.
    /// </summary>
    private const string SecretText = "Host=db.internal;Username=carpos_be;Password=hunter2";

    [Fact]
    public async Task WritesAnOpaqueFiveHundred()
    {
        RecordingLogger<GlobalExceptionHandler> logger = new RecordingLogger<GlobalExceptionHandler>();
        DefaultHttpContext context = NewContext();

        bool handled = await NewHandler(logger).TryHandleAsync(
            context,
            new InvalidOperationException(SecretText),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        JsonElement body = ReadBody(context);
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.Equal("Server error", body.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task NeverPutsTheExceptionOnTheWire()
    {
        DefaultHttpContext context = NewContext();

        // Nested and with a real stack trace, because that is the realistic shape:
        // the revealing text is usually in an inner exception, not the outer one.
        Exception inner = Caught(() => throw new InvalidOperationException(SecretText));
        Exception exception = new Exception("Saving the entity failed. See the inner exception.", inner);

        await NewHandler(new RecordingLogger<GlobalExceptionHandler>())
            .TryHandleAsync(context, exception, CancellationToken.None);

        string body = ReadBodyText(context);

        Assert.DoesNotContain(SecretText, body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db.internal", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CarPosAPI", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(GlobalExceptionHandlerTests), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsTheExceptionItRefusesToReturn()
    {
        // The other half of the contract. What is kept off the wire has to be in the
        // log, or an opaque 500 becomes an unactionable one.
        RecordingLogger<GlobalExceptionHandler> logger = new RecordingLogger<GlobalExceptionHandler>();

        Exception exception = new InvalidOperationException(SecretText);

        await NewHandler(logger).TryHandleAsync(NewContext(), exception, CancellationToken.None);

        LogEntry entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(exception, entry.Exception);
    }

    [Fact]
    public async Task LeavesACancelledRequestAlone()
    {
        // A closed tab is not a fault. Handling it would write a 500 nobody is
        // listening for, and logging it as an error would bury the real ones.
        RecordingLogger<GlobalExceptionHandler> logger = new RecordingLogger<GlobalExceptionHandler>();
        DefaultHttpContext context = NewContext();

        using CancellationTokenSource cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        bool handled = await NewHandler(logger).TryHandleAsync(
            context,
            new OperationCanceledException(),
            cancelled.Token);

        Assert.False(handled);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task AbortsInsteadOfWritingOverAStartedResponse()
    {
        // In the live pipeline the exception handler middleware catches this case
        // before the handler is reached: it tests HasStarted, logs, and rethrows so
        // Kestrel aborts. This test covers the handler called directly, which is the
        // only way the branch runs — and the reason it exists, since without it a
        // direct call would throw from inside an exception handler. Touching the
        // status code on a started response throws, which is what StartedResponseFeature
        // below asserts the handler does not do.
        RecordingLogger<GlobalExceptionHandler> logger = new RecordingLogger<GlobalExceptionHandler>();

        StartedResponseFeature response = new StartedResponseFeature();
        AbortRecordingLifetimeFeature lifetime = new AbortRecordingLifetimeFeature();

        DefaultHttpContext context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);

        bool handled = await NewHandler(logger).TryHandleAsync(
            context,
            new InvalidOperationException(SecretText),
            CancellationToken.None);

        Assert.True(handled);
        Assert.True(lifetime.Aborted);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    [InlineData(StatusCodes.Status431RequestHeaderFieldsTooLarge)]
    [InlineData(StatusCodes.Status400BadRequest)]
    public async Task KeepsTheStatusKestrelChoseForAMalformedRequest(int statusCode)
    {
        // The caller broke the request, so a 500 would be both wrong and misleading:
        // it blames the server and invites a retry guaranteed to fail the same way.
        RecordingLogger<GlobalExceptionHandler> logger = new RecordingLogger<GlobalExceptionHandler>();
        DefaultHttpContext context = NewContext();

        bool handled = await NewHandler(logger).TryHandleAsync(
            context,
            new BadHttpRequestException(
                "Request body too large. The max request body size is 262144 bytes.",
                statusCode),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(statusCode, ReadBody(context).GetProperty("status").GetInt32());

        // Warning rather than Error: this is not a fault in this application, though
        // a run of them is still worth being able to see.
        LogEntry entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public async Task DoesNotEchoTheLimitBackToAMalformedRequest()
    {
        // Kestrel's message names the exact byte limit that was hit. Handing that to a
        // caller tells them precisely where the boundary sits, which is the first
        // thing anyone probing for one wants to know.
        DefaultHttpContext context = NewContext();

        await NewHandler(new RecordingLogger<GlobalExceptionHandler>()).TryHandleAsync(
            context,
            new BadHttpRequestException(
                "Request body too large. The max request body size is 262144 bytes.",
                StatusCodes.Status413PayloadTooLarge),
            CancellationToken.None);

        string body = ReadBodyText(context);

        Assert.DoesNotContain("262144", body, StringComparison.Ordinal);
        Assert.Contains("too large", body, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    /// <summary>Builds the handler under test with a stub ProblemDetails writer.</summary>
    /// <param name="logger">The recorder the assertions read back.</param>
    /// <returns>A handler wired for one test.</returns>
    private static GlobalExceptionHandler NewHandler(RecordingLogger<GlobalExceptionHandler> logger)
    {
        return new GlobalExceptionHandler(logger, new StubProblemDetailsService());
    }

    /// <summary>A context whose response body can be read back afterwards.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext NewContext()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/devices";
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>Reads the written body as text.</summary>
    /// <param name="context">The answered context.</param>
    /// <returns>The raw response body.</returns>
    private static string ReadBodyText(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using StreamReader reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }

    /// <summary>Reads the written body as parsed JSON.</summary>
    /// <param name="context">The answered context.</param>
    /// <returns>The body's root element.</returns>
    private static JsonElement ReadBody(DefaultHttpContext context)
    {
        return JsonDocument.Parse(ReadBodyText(context)).RootElement.Clone();
    }

    /// <summary>Throws and catches, so the exception carries a real stack trace.</summary>
    /// <param name="thrower">An action that throws.</param>
    /// <returns>The caught exception.</returns>
    private static Exception Caught(Action thrower)
    {
        try
        {
            thrower();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The action did not throw.");
    }

    /// <summary>One captured log call.</summary>
    /// <param name="Level">The level it was logged at.</param>
    /// <param name="Exception">The exception attached, if any.</param>
    /// <param name="Message">The formatted message.</param>
    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);

    /// <summary>Captures log calls instead of writing them anywhere.</summary>
    /// <typeparam name="TCategory">The logger's category type.</typeparam>
    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        /// <summary>Everything logged, in order.</summary>
        public List<LogEntry> Entries { get; } = new List<LogEntry>();

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    /// <summary>
    /// Serialises whatever it is handed straight to the body, which is what the real
    /// writer does once content negotiation has settled on JSON. Standing in for it
    /// keeps these tests clear of the MVC formatter stack.
    /// </summary>
    private sealed class StubProblemDetailsService : IProblemDetailsService
    {
        /// <inheritdoc />
        public async ValueTask WriteAsync(ProblemDetailsContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.HttpContext.Response.ContentType = "application/problem+json";

            await JsonSerializer.SerializeAsync(
                context.HttpContext.Response.Body,
                context.ProblemDetails,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        /// <inheritdoc />
        public async ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            await WriteAsync(context);
            return true;
        }
    }

    /// <summary>
    /// A response that has already gone out. Setting the status code on one throws in
    /// Kestrel, so it throws here too — that is the assertion.
    /// </summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        /// <inheritdoc />
        public int StatusCode
        {
            get => StatusCodes.Status200OK;
            set => throw new InvalidOperationException(
                "StatusCode cannot be set because the response has already started.");
        }

        /// <inheritdoc />
        public string? ReasonPhrase { get; set; }

        /// <inheritdoc />
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        /// <inheritdoc />
        public Stream Body { get; set; } = Stream.Null;

        /// <inheritdoc />
        public bool HasStarted => true;

        /// <inheritdoc />
        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        /// <inheritdoc />
        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    /// <summary>Records whether the connection was aborted.</summary>
    private sealed class AbortRecordingLifetimeFeature : IHttpRequestLifetimeFeature
    {
        /// <summary>True once <see cref="Abort"/> has been called.</summary>
        public bool Aborted { get; private set; }

        /// <inheritdoc />
        public CancellationToken RequestAborted { get; set; } = CancellationToken.None;

        /// <inheritdoc />
        public void Abort() => Aborted = true;
    }
}
