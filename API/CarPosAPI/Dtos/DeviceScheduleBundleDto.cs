using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// The whole schedule as it goes onto <c>devices/&lt;id&gt;/schedule</c>, retained.
///
/// <para>
/// This is what moved the switching onto the device. Until it existed the firmware
/// never learned a schedule was involved at all: the server evaluated the rules and
/// republished a plain settings document, so a tracker only ever changed profile while
/// the broker could reach it — which is exactly the wrong property for a car parked in
/// an underground garage overnight.
/// </para>
///
/// <para>
/// Like <see cref="DeviceConfigDocumentDto"/>, this shape is owned by the firmware:
/// <c>ESP32/src/settings/ScheduleCodec.cpp</c> is its only decoder and the two must
/// match character for character. It is likewise <b>plaintext</b> — it carries
/// settings and windows, never a position — with the broker hop protected by TLS.
/// </para>
///
/// <para>
/// <c>devices/&lt;id&gt;/config</c> is untouched by all of this and still carries the
/// document it always did. It remains the fallback for a device with no schedule, the
/// channel a correction travels on, and the reason old firmware keeps working
/// unaltered.
/// </para>
///
/// <para>
/// Two encoding choices worth stating, both made to keep the device's parser free of
/// branches it could get wrong. <see cref="FallbackSlot"/> uses
/// <see cref="NoFallbackSlot"/> rather than null, so the device reads an integer and
/// compares it, never inspecting a JSON null. <see cref="Override"/> is genuinely
/// absent when there is none, so the device's check is "is the key there?" — which
/// means the publisher must serialize with
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/>.
/// </para>
/// </summary>
/// <param name="ScheduleVersion">Bumped on every change; the device echoes it back so the server can tell a stale bundle from a genuine disagreement.</param>
/// <param name="Enabled">False disables device-side switching entirely — the device falls back to the retained config document.</param>
/// <param name="FallbackSlot">Profile applied wherever no window covers the instant, or <see cref="NoFallbackSlot"/>.</param>
/// <param name="Profiles">The device's profiles, at most <see cref="ScheduleRules.MaxProfilesPerDevice"/> of them.</param>
/// <param name="Rules">The enabled windows, at most <see cref="ScheduleRules.MaxRulesPerDevice"/> of them.</param>
/// <param name="Override">Values that beat the schedule until a stated instant, or null.</param>
public sealed record DeviceScheduleBundleDto(
    [property: JsonPropertyName("sched_v")] int ScheduleVersion,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("fallback")] int FallbackSlot,
    [property: JsonPropertyName("profiles")] IReadOnlyList<ScheduleBundleProfileDto> Profiles,
    [property: JsonPropertyName("rules")] IReadOnlyList<ScheduleBundleRuleDto> Rules,
    [property: JsonPropertyName("override")] ScheduleBundleOverrideDto? Override)
{
    /// <summary>
    /// The <see cref="FallbackSlot"/> value meaning "no fallback profile". Negative so
    /// it can never collide with a real slot, which is always non-negative.
    /// </summary>
    public const int NoFallbackSlot = -1;

    /// <summary>
    /// How this document must be serialized to be readable by the firmware.
    ///
    /// <para>
    /// It lives here rather than in the publisher because it is part of the wire
    /// contract, not a formatting preference: <c>ScheduleCodec</c> tests for the
    /// presence of the <c>override</c> key, so emitting <c>"override": null</c> would
    /// be read as an override with no expiry and no values. Keeping it on the DTO is
    /// also what lets a test assert the exact bytes the device will see.
    /// </para>
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
