using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// The decrypted inner payload of a device status message, as the firmware serialises
/// it (<c>ESP32/src/mqtt/StatusPublisher.cpp</c>) and publishes to
/// <c>devices/&lt;id&gt;/status</c> — the device's own "online" and "offline because …"
/// messages, and its Last Will, which the broker publishes on its behalf.
///
/// Every member is nullable so absence is a decision in
/// <see cref="Services.Ingest.DeviceStatusValidator"/> rather than a serializer crash —
/// the same approach as <see cref="PositionPayloadDto"/>. Unlike a position it carries
/// no location, but it still says when a vehicle was in use, so it is not logged either.
/// </summary>
/// <param name="Device">Device id claimed inside the encrypted payload; must match the topic.</param>
/// <param name="Type"><c>online</c> or <c>offline</c>.</param>
/// <param name="Reason">
/// Why the device went offline (<c>sleep</c>, <c>power_off</c>, <c>battery_low</c>,
/// <c>error</c>, <c>connection_lost</c>). Required for <c>offline</c>, ignored for
/// <c>online</c>.
/// </param>
/// <param name="TimeUtc">
/// Device clock at the time, ISO-8601 UTC with second precision — only sent while the
/// device trusts its clock, and never in the Last Will, which is sealed long before the
/// broker publishes it.
/// </param>
/// <param name="BatteryPct">Last known battery percent; 0 is the charging sentinel (optional).</param>
/// <param name="SleepSeconds">How long the device expects to be away, in seconds (optional).</param>
/// <param name="ResetReason">
/// The chip's reset cause on the first connection after a boot — <c>POWERON</c>,
/// <c>BROWNOUT</c>, <c>PANIC</c>, <c>TASK_WDT</c>, <c>DEEPSLEEP</c> … (optional).
/// </param>
/// <param name="Detail">A short machine code qualifying an <c>error</c>, e.g. <c>gnss_init</c> (optional).</param>
public sealed record DeviceStatusPayloadDto(
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("time_utc")] string? TimeUtc = null,
    [property: JsonPropertyName("battery_pct")] int? BatteryPct = null,
    [property: JsonPropertyName("sleep_s")] int? SleepSeconds = null,
    [property: JsonPropertyName("reset_reason")] string? ResetReason = null,
    [property: JsonPropertyName("detail")] string? Detail = null);
