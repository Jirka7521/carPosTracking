using System.Text.Json;
using CarPosAPI.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Services.Health;

/// <summary>
/// Turns the framework's <see cref="HealthReport"/> into the JSON body of
/// <c>/health</c>. Wired up as the <c>ResponseWriter</c> in <c>Program.cs</c>.
///
/// <para>
/// Without this, ASP.NET Core writes the single word <c>Healthy</c> and discards
/// everything the checks found — which is why the ingest counters have been
/// collected since the first release and never been visible. This writer is the
/// whole difference between "something is wrong" and "the broker link is down,
/// the last message arrived eight minutes ago, and the database is fine".
/// </para>
///
/// <para>
/// <b>It is a filter, not a dump.</b> The endpoint is unauthenticated, so
/// <see cref="HealthReportEntry.Exception"/> is deliberately dropped: an Npgsql
/// failure message carries the host, the database and the role, and a JWT or key
/// fault can carry worse. Only <see cref="HealthReportEntry.Description"/> and
/// <see cref="HealthReportEntry.Data"/> are copied, and every check in
/// <c>Services/Health/</c> writes both itself. Tags are dropped too — they are a
/// server-side routing concern with nothing in them for a caller.
/// </para>
/// </summary>
internal static class HealthReportWriter
{
    /// <summary>
    /// Durations are rounded to hundredths of a millisecond. The extra digits are
    /// scheduling noise, and they make two probes impossible to eyeball as
    /// "the same".
    /// </summary>
    private const int DurationDecimals = 2;

    /// <summary>
    /// Web defaults: camelCase names, which is the JSON policy the whole API is
    /// held to. Static because <see cref="JsonSerializerOptions"/> caches its
    /// metadata and a fresh instance per probe would throw that away every time.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    /// Writes the report as JSON. The status code has already been set by the
    /// health-check middleware from the overall status (200 for Healthy and
    /// Degraded, 503 for Unhealthy) — this only fills in the body.
    /// </summary>
    /// <param name="context">The probe's request; its body is the destination.</param>
    /// <param name="report">The completed report from all registered checks.</param>
    /// <returns>A task that completes when the body has been written.</returns>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        // A cached health answer is a lie about the present, and probes are the one
        // thing that must never be served from anyone's buffer.
        context.Response.Headers.CacheControl = "no-store, no-cache";

        Dictionary<string, HealthCheckEntryDto> checks =
            new Dictionary<string, HealthCheckEntryDto>(report.Entries.Count, StringComparer.Ordinal);

        foreach (KeyValuePair<string, HealthReportEntry> pair in report.Entries)
        {
            HealthReportEntry entry = pair.Value;

            checks[pair.Key] = new HealthCheckEntryDto(
                entry.Status.ToString(),
                Math.Round(entry.Duration.TotalMilliseconds, DurationDecimals),
                entry.Description,
                // Empty is the framework's "no data", and an empty object in the
                // output would suggest the check had something to say and didn't.
                entry.Data.Count == 0 ? null : entry.Data);
        }

        HealthReportDto body = new HealthReportDto(
            report.Status.ToString(),
            DateTime.UtcNow,
            Math.Round(report.TotalDuration.TotalMilliseconds, DurationDecimals),
            checks);

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            body,
            s_jsonOptions,
            context.RequestAborted);
    }
}
