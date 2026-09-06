using CarPosAPI.Services.Ingest;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="MqttIngestHealthCheck"/> — the two-state verdict and, since
/// the health endpoint began emitting JSON, the structured facts it carries.
///
/// <para>
/// The status half matters because it decides an HTTP code: Degraded answers 200
/// and leaves the container up, which is the deliberate choice for a link whose
/// reconnect loop is self-healing. A change to Unhealthy here would make a broker
/// blip restart the API, so it is worth a test that says so out loud.
/// </para>
/// </summary>
public sealed class MqttIngestHealthCheckTests
{
    private static readonly HealthCheckContext Context = new HealthCheckContext();

    [Fact]
    public async Task Connected_IsHealthy()
    {
        MqttConnectionState state = new MqttConnectionState();
        state.SetConnected(true);

        HealthCheckResult result = await new MqttIngestHealthCheck(state).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(true, result.Data["connected"]);
    }

    [Fact]
    public async Task Disconnected_IsDegradedRatherThanUnhealthy()
    {
        MqttConnectionState state = new MqttConnectionState();
        state.SetConnected(false);

        HealthCheckResult result = await new MqttIngestHealthCheck(state).CheckHealthAsync(Context);

        // Not Unhealthy: that would be a 503, and a 503 restarts the container over
        // an outage the reconnect loop fixes by itself.
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(false, result.Data["connected"]);
    }

    [Fact]
    public async Task CountersReachTheReportAsNumbers()
    {
        MqttConnectionState state = new MqttConnectionState();
        state.SetConnected(true);
        state.RecordMessage();
        state.RecordMessage();
        state.RecordOutcome(inserted: 3, duplicates: 2, rejected: 1);

        HealthCheckResult result = await new MqttIngestHealthCheck(state).CheckHealthAsync(Context);

        Assert.Equal(2L, result.Data["messagesReceived"]);
        Assert.Equal(3L, result.Data["positionsInserted"]);
        Assert.Equal(2L, result.Data["positionsDuplicate"]);
        Assert.Equal(1L, result.Data["envelopesRejected"]);
        Assert.True(result.Data.ContainsKey("lastMessageAtUtc"));
        Assert.True(result.Data.ContainsKey("secondsSinceLastMessage"));
    }

    [Fact]
    public async Task BeforeTheFirstMessage_TheTimestampIsAbsentRatherThanEpoch()
    {
        MqttConnectionState state = new MqttConnectionState();
        state.SetConnected(true);

        HealthCheckResult result = await new MqttIngestHealthCheck(state).CheckHealthAsync(Context);

        // An absent key reads as "never"; year 1 or 1970 would read as a real fix
        // that arrived a very long time ago.
        Assert.False(result.Data.ContainsKey("lastMessageAtUtc"));
        Assert.Equal(0L, result.Data["messagesReceived"]);
    }
}
