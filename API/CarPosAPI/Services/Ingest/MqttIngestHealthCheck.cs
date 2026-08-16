using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Reports the MQTT link on <c>/health</c>. Degraded (not Unhealthy) while
/// disconnected: the reconnect loop is self-healing and messages queue in the
/// broker's persistent session meanwhile, so a blip is worth surfacing but is
/// not an outage.
/// </summary>
internal sealed class MqttIngestHealthCheck : IHealthCheck
{
    private readonly MqttConnectionState _state;

    /// <summary>Creates the health check.</summary>
    /// <param name="state">Shared connection-state snapshot.</param>
    public MqttIngestHealthCheck(MqttConnectionState state)
    {
        _state = state;
    }

    /// <summary>Evaluates the current MQTT connection state.</summary>
    /// <param name="context">Health-check context (unused).</param>
    /// <param name="cancellationToken">Not used — the check reads counters only.</param>
    /// <returns>Healthy when connected, Degraded otherwise.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        string description =
            $"connected={_state.IsConnected}, messages={_state.MessagesReceived}, " +
            $"inserted={_state.PositionsInserted}, duplicates={_state.PositionsDuplicate}, " +
            $"rejected={_state.EnvelopesRejected}, lastMessageUtc={_state.LastMessageAtUtc:O}";

        HealthCheckResult result = _state.IsConnected
            ? HealthCheckResult.Healthy(description)
            : HealthCheckResult.Degraded(description);
        return Task.FromResult(result);
    }
}
