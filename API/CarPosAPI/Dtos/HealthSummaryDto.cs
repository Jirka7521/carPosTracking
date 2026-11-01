namespace CarPosAPI.Dtos;

/// <summary>
/// The body of <c>GET /health</c> for a caller outside the container: the overall
/// verdict and nothing else. Everything the full <see cref="HealthReportDto"/> adds
/// — check names, versions, counters, pending migrations — is operator detail, and
/// the tunnel publishes this endpoint to the internet.
/// </summary>
/// <param name="Status">Healthy, Degraded or Unhealthy — the same value the full report leads with.</param>
public sealed record HealthSummaryDto(string Status);
