using System.Text;
using System.Text.Json;
using CarPosAPI.Services.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="HealthReportWriter"/> — the shape of the <c>/health</c> body
/// and, more importantly, what it refuses to put in it.
///
/// <para>
/// The redaction test is the reason this class exists. <c>/health</c> is
/// unauthenticated, and the framework's own entries carry the raw exception: an
/// Npgsql failure message names the host, the database and the role. A writer that
/// serialised it would leak that to anyone who could reach the port, and nothing
/// else in the system would notice.
/// </para>
/// </summary>
public sealed class HealthReportWriterTests
{
    private const string SecretText = "Host=db.internal;Username=carpos_be;Password=hunter2";

    [Fact]
    public async Task WritesTheOverallStatusAndOneEntryPerCheck()
    {
        HealthReport report = ReportWith(
            ("database", Entry(HealthStatus.Healthy, "connection ok", new Dictionary<string, object>
            {
                ["latencyMs"] = 11.2,
            })),
            ("mqtt", Entry(HealthStatus.Degraded, "disconnected", new Dictionary<string, object>
            {
                ["connected"] = false,
                ["messagesReceived"] = 1204L,
            })));

        JsonElement root = await WriteAndParseAsync(report);

        Assert.Equal("Degraded", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("totalDurationMs").GetDouble() >= 0);
        Assert.True(root.TryGetProperty("checkedAtUtc", out _));

        JsonElement checks = root.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetProperty("status").GetString());
        Assert.Equal(11.2, checks.GetProperty("database").GetProperty("data").GetProperty("latencyMs").GetDouble());

        JsonElement mqtt = checks.GetProperty("mqtt");
        Assert.Equal("Degraded", mqtt.GetProperty("status").GetString());
        Assert.Equal("disconnected", mqtt.GetProperty("description").GetString());
        Assert.False(mqtt.GetProperty("data").GetProperty("connected").GetBoolean());
        Assert.Equal(1204, mqtt.GetProperty("data").GetProperty("messagesReceived").GetInt64());
    }

    [Fact]
    public async Task NeverEmitsTheException()
    {
        HealthReport report = ReportWith(
            ("database", new HealthReportEntry(
                HealthStatus.Unhealthy,
                "unreachable",
                TimeSpan.FromMilliseconds(5001),
                new InvalidOperationException(SecretText),
                data: null)));

        (JsonElement root, string raw) = await WriteAndParseWithRawAsync(report);

        Assert.DoesNotContain("hunter2", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db.internal", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", raw, StringComparison.OrdinalIgnoreCase);

        // The safe half still gets through, or the endpoint would say nothing useful.
        JsonElement database = root.GetProperty("checks").GetProperty("database");
        Assert.Equal("Unhealthy", database.GetProperty("status").GetString());
        Assert.Equal("unreachable", database.GetProperty("description").GetString());
    }

    [Fact]
    public async Task AnEmptyDataBagIsWrittenAsNullRatherThanAnEmptyObject()
    {
        HealthReport report = ReportWith(("process", Entry(HealthStatus.Healthy, "running", null)));

        JsonElement root = await WriteAndParseAsync(report);

        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("checks").GetProperty("process").GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task SetsAJsonContentTypeAndForbidsCaching()
    {
        DefaultHttpContext context = ContextWithBodyBuffer();

        await HealthReportWriter.WriteAsync(context, ReportWith(("process", Entry(HealthStatus.Healthy, null, null))));

        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.Contains("no-store", context.Response.Headers.CacheControl.ToString());
    }

    /// <summary>Builds a report from named entries, with a plausible total duration.</summary>
    /// <param name="entries">The per-check results.</param>
    /// <returns>A report the writer can serialise.</returns>
    private static HealthReport ReportWith(params (string Name, HealthReportEntry Entry)[] entries)
    {
        Dictionary<string, HealthReportEntry> map = new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal);

        foreach ((string name, HealthReportEntry entry) in entries)
        {
            map[name] = entry;
        }

        return new HealthReport(map, TimeSpan.FromMilliseconds(12.4));
    }

    /// <summary>Builds one entry with no exception.</summary>
    /// <param name="status">The entry's status.</param>
    /// <param name="description">Its description, or null.</param>
    /// <param name="data">Its data bag, or null for none.</param>
    /// <returns>The entry.</returns>
    private static HealthReportEntry Entry(
        HealthStatus status,
        string? description,
        IReadOnlyDictionary<string, object>? data)
    {
        return new HealthReportEntry(status, description, TimeSpan.FromMilliseconds(1), null, data);
    }

    /// <summary>An HTTP context whose response body can be read back.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext ContextWithBodyBuffer()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>Serialises a report and parses the result.</summary>
    /// <param name="report">The report to write.</param>
    /// <returns>The parsed root element.</returns>
    private static async Task<JsonElement> WriteAndParseAsync(HealthReport report)
    {
        (JsonElement root, string _) = await WriteAndParseWithRawAsync(report);
        return root;
    }

    /// <summary>Serialises a report and returns both the parsed body and its raw text.</summary>
    /// <param name="report">The report to write.</param>
    /// <returns>The parsed root element and the raw JSON, for substring assertions.</returns>
    private static async Task<(JsonElement Root, string Raw)> WriteAndParseWithRawAsync(HealthReport report)
    {
        DefaultHttpContext context = ContextWithBodyBuffer();

        await HealthReportWriter.WriteAsync(context, report);

        MemoryStream body = (MemoryStream)context.Response.Body;
        string raw = Encoding.UTF8.GetString(body.ToArray());

        // Parsed into a document that outlives this method: JsonDocument owns the
        // element, so it is cloned rather than returned from a disposed document.
        using JsonDocument document = JsonDocument.Parse(raw);
        return (document.RootElement.Clone(), raw);
    }
}
