namespace CarPosAPI.Dtos;

/// <summary>
/// One dependency's verdict inside the <see cref="HealthReportDto"/> body of
/// <c>/health</c>.
///
/// <para>
/// <b>There is deliberately no exception field.</b> The endpoint is
/// unauthenticated, so everything it emits is public to anyone who can reach the
/// port. A framework exception message would happily print the connection string,
/// the database host, the role name or the failing SQL — which is precisely the
/// reconnaissance an attacker wants and precisely what
/// <see cref="Services.Health.HealthReportWriter"/> refuses to copy across.
/// <see cref="Description"/> is only ever a short phrase this codebase wrote.
/// </para>
///
/// <para>
/// <see cref="Data"/> is left as an open bag of primitives rather than a typed
/// shape per check: each check knows facts nothing else has (broker counters,
/// pending migration ids, uptime) and a union type over all of them would be a
/// record of mostly-null fields that has to be edited every time a check is added.
/// </para>
/// </summary>
/// <param name="Status">Healthy, Degraded or Unhealthy.</param>
/// <param name="DurationMs">How long this one check took, in milliseconds.</param>
/// <param name="Description">Short human explanation, or null when the status says it all.</param>
/// <param name="Data">Check-specific facts — JSON primitives only. Null when the check reports none.</param>
public sealed record HealthCheckEntryDto(
    string Status,
    double DurationMs,
    string? Description,
    IReadOnlyDictionary<string, object>? Data);
