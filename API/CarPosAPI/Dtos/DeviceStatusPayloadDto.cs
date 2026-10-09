using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// The decrypted inner payload of a device status message, as the firmware serialises
/// it (<c>ESP32/src/mqtt/StatusPublisher.cpp</c>) and publishes to
/// <c>devices/&lt;id&gt;/status</c> — the device's own "online", "offline because …",
/// "woke because …" and motion-step messages, and its Last Will, which the broker
/// publishes on its behalf. All but "online" may have waited on the device's SD card
/// and arrive in a burst with others, long after they happened.
///
/// Every member is nullable so absence is a decision in
/// <see cref="Services.Ingest.DeviceStatusValidator"/> rather than a serializer crash —
/// the same approach as <see cref="PositionPayloadDto"/>. Unlike a position it carries
/// no location, but it still says when a vehicle was in use, so it is not logged either.
/// </summary>
/// <param name="Device">Device id claimed inside the encrypted payload; must match the topic.</param>
/// <param name="Type"><c>online</c>, <c>offline</c>, <c>wake</c> or <c>motion</c>.</param>
/// <param name="Reason">
/// What happened. For <c>offline</c> why the device went away (<c>sleep</c>,
/// <c>sleep_no_motion</c>, <c>power_off</c>, <c>battery_low</c>, <c>error</c>,
/// <c>connection_lost</c>); for <c>wake</c> what woke it (<c>timer</c>,
/// <c>accelerometer</c>, <c>power_switch</c>); for <c>motion</c> which step its
/// motion-wake state machine took (<c>checking</c>, <c>activity</c>, <c>motion_on</c>,
/// <c>moving</c>, <c>no_motion</c>, <c>stopped</c>, <c>motion_off</c>). Required for all
/// three, ignored for <c>online</c>.
/// </param>
/// <param name="TimeUtc">
/// Device clock at the time, ISO-8601 UTC with second precision — only sent while the
/// device trusts its clock, and never in the Last Will, which is sealed long before the
/// broker publishes it. The only record of when a message that sat on the card happened.
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
