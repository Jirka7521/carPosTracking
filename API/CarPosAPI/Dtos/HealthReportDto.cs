namespace CarPosAPI.Dtos;

/// <summary>
/// The body of <c>GET /health</c>: one overall verdict plus a separate entry per
/// dependency (database, MQTT broker, migrations, schedule worker, process).
///
/// <para>
/// A map keyed by check name rather than an array, because a caller almost always
/// wants one named check — <c>checks.mqtt.data.connected</c> reads directly, while
/// an array forces every consumer to write the same find-by-name loop.
/// </para>
///
/// <para>
/// The overall <see cref="Status"/> is the worst of the entries, and it is what
/// decides the HTTP status code: Healthy and Degraded both answer 200, only
/// Unhealthy answers 503. That split is the contract the container healthcheck
/// reads — see <see cref="Services.Health.HealthReportWriter"/>.
/// </para>
/// </summary>
/// <param name="Status">The worst status among the checks: Healthy, Degraded or Unhealthy.</param>
/// <param name="CheckedAtUtc">When the probe ran (UTC), so a cached or proxied answer is recognisable as stale.</param>
/// <param name="TotalDurationMs">Wall-clock milliseconds for the whole probe.</param>
/// <param name="Checks">Per-dependency results, keyed by check name.</param>
public sealed record HealthReportDto(
    string Status,
    DateTime CheckedAtUtc,
    double TotalDurationMs,
    IReadOnlyDictionary<string, HealthCheckEntryDto> Checks);
