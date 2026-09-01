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
        bool connected = _state.IsConnected;
        DateTime? lastMessageAtUtc = _state.LastMessageAtUtc;

        // Structured rather than the one interpolated string this used to build:
        // the response writer emits this dictionary as JSON, so a caller can read
        // one counter without parsing a sentence. The broker URI, username and
        // client id are deliberately absent — this endpoint is unauthenticated.
        Dictionary<string, object> data = new Dictionary<string, object>
        {
            ["connected"] = connected,
            ["messagesReceived"] = _state.MessagesReceived,
            ["positionsInserted"] = _state.PositionsInserted,
            ["positionsDuplicate"] = _state.PositionsDuplicate,
            ["envelopesRejected"] = _state.EnvelopesRejected,
        };

        // Absent rather than null before the first message ever arrives; after one,
        // its age is the number that says whether a "connected" link is actually
        // carrying anything.
        if (lastMessageAtUtc.HasValue)
        {
            data["lastMessageAtUtc"] = lastMessageAtUtc.Value;
            data["secondsSinceLastMessage"] =
                Math.Round((DateTime.UtcNow - lastMessageAtUtc.Value).TotalSeconds, 1);
        }

        HealthCheckResult result = connected
            ? HealthCheckResult.Healthy("broker connected", data)
            : HealthCheckResult.Degraded("disconnected; the reconnect loop is running", data: data);

        return Task.FromResult(result);
    }
}
