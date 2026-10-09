using System.Text.Json;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Scheduling;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the schedule bundle's wire format.
///
/// <para>
/// This shape is owned by the firmware: <c>ESP32/src/settings/ScheduleCodec.cpp</c> is
/// its only decoder, and it is strict — a renamed key, a JSON null where a key should
/// be absent, or an instant in the wrong format does not produce an error anybody sees.
/// The device logs a line to a serial console nobody is watching, keeps running the
/// configuration document instead, and looks completely healthy. These tests are the
/// thing standing between a refactor and that.
/// </para>
/// </summary>
public class ScheduleBundleSerializationTests
{
    /// <summary>
    /// The profile's motion block: the firmware defaults with motion switched on. Built
    /// from the DTO constructors directly rather than through the factory, so these
    /// tests state the wire contract independently of the code that fills it in.
    /// </summary>
    private static readonly DeviceMotionDocumentDto s_profileMotion = new DeviceMotionDocumentDto(
        true,
        63,
        3,
        240,
        600,
        new DeviceModeDocumentDto(10, false, 180));

    /// <summary>
    /// The override's motion block, with every value different from
    /// <see cref="s_profileMotion"/> so that the two blocks cannot be mistaken for one
    /// another in the serialized text.
    /// </summary>
    private static readonly DeviceMotionDocumentDto s_overrideMotion = new DeviceMotionDocumentDto(
        false,
        125,
        5,
        300,
        900,
        new DeviceModeDocumentDto(20, true, 120));

    /// <summary>Builds a bundle with one profile and one rule.</summary>
    /// <param name="withOverride">Whether to attach an override.</param>
    /// <returns>The bundle.</returns>
    private static DeviceScheduleBundleDto Sample(bool withOverride)
    {
        List<ScheduleBundleProfileDto> profiles = new List<ScheduleBundleProfileDto>
        {
            new ScheduleBundleProfileDto(0, "Night", 300, true, 180, 20000, 24, 168, 3600, s_profileMotion),
        };

        List<ScheduleBundleRuleDto> rules = new List<ScheduleBundleRuleDto>
        {
            new ScheduleBundleRuleDto(0, 62, 1320, 480, 100, 3),
        };

        ScheduleBundleOverrideDto? liveOverride = withOverride
            ? new ScheduleBundleOverrideDto(
                ScheduleBundleBuilder.FormatDeviceInstant(
                    new DateTime(2026, 9, 6, 22, 0, 0, DateTimeKind.Utc)),
                30, false, 180, 20000, 24, 168, 3600, s_overrideMotion)
            : null;

        return new DeviceScheduleBundleDto(7, true, 0, profiles, rules, liveOverride);
    }

    [Fact]
    public void EveryKeyIsSpelledExactlyAsTheFirmwareExpects()
    {
        string json = JsonSerializer.Serialize(
            Sample(withOverride: true), DeviceScheduleBundleDto.SerializerOptions);

        // Spelled out rather than looped, so a rename shows up here as a failing
        // assertion naming the key rather than as a device that silently stops
        // switching profiles.
        Assert.Contains("\"sched_v\":7", json);
        Assert.Contains("\"enabled\":true", json);
        Assert.Contains("\"fallback\":0", json);
        Assert.Contains("\"profiles\":[", json);
        Assert.Contains("\"rules\":[", json);
        Assert.Contains("\"slot\":0", json);
        Assert.Contains("\"name\":\"Night\"", json);
        Assert.Contains("\"days\":62", json);
        Assert.Contains("\"start_m\":1320", json);
        Assert.Contains("\"dur_m\":480", json);
        Assert.Contains("\"prio\":100", json);
        Assert.Contains("\"ord\":3", json);

        // The seven value keys are shared with DeviceConfigDocumentDto on purpose:
        // the firmware hands a profile object straight to SettingsCodec::decodeObject.
        Assert.Contains("\"interval_s\":300", json);
        Assert.Contains("\"sleep_between\":true", json);
        Assert.Contains("\"fix_timeout_s\":180", json);
        Assert.Contains("\"queue_max_fixes\":20000", json);
        Assert.Contains("\"retry_interval_h\":24", json);
        Assert.Contains("\"retry_max_age_h\":168", json);
        Assert.Contains("\"config_check_s\":3600", json);
    }

    [Fact]
    public void AProfileCarriesItsMotionBlockAfterConfigCheckWithTheExactKeys()
    {
        string json = JsonSerializer.Serialize(
            Sample(withOverride: false), DeviceScheduleBundleDto.SerializerOptions);

        // The motion block rides inside every profile so that a schedule switch changes
        // the whole motion configuration along with the standby values, and the firmware
        // hands the profile object to the same SettingsCodec::decode that reads the
        // retained config document — so the block must follow config_check_s and be
        // spelled exactly as that decoder expects. Asserted as one string, order
        // included, because a reordered key is not a failure anyone would see on the
        // device.
        Assert.Contains(
            "\"config_check_s\":3600,\"motion\":{"
                + "\"enabled\":true,\"threshold_mg\":63,\"speed_kmph\":3,"
                + "\"wake_wait_s\":240,\"stop_wait_s\":600,"
                + "\"moving\":{\"interval_s\":10,\"sleep_between\":false,\"fix_timeout_s\":180}}",
            json,
            StringComparison.Ordinal);

        // And the individual keys by name, so a rename fails with a message that says
        // which key rather than a diff of two long strings.
        Assert.Contains("\"motion\":{", json);
        Assert.Contains("\"enabled\":true", json);
        Assert.Contains("\"threshold_mg\":63", json);
        Assert.Contains("\"speed_kmph\":3", json);
        Assert.Contains("\"wake_wait_s\":240", json);
        Assert.Contains("\"stop_wait_s\":600", json);
        Assert.Contains("\"moving\":{", json);
    }

    [Fact]
    public void AnOverrideCarriesItsOwnMotionBlockAfterConfigCheck()
    {
        string json = JsonSerializer.Serialize(
            Sample(withOverride: true), DeviceScheduleBundleDto.SerializerOptions);

        // The override beats the schedule wholesale, so it has to carry the motion
        // block too: otherwise a manual save on a scheduled device would switch the
        // standby values and quietly leave the previous profile's motion settings
        // running underneath them.
        Assert.Contains(
            "\"config_check_s\":3600,\"motion\":{"
                + "\"enabled\":false,\"threshold_mg\":125,\"speed_kmph\":5,"
                + "\"wake_wait_s\":300,\"stop_wait_s\":900,"
                + "\"moving\":{\"interval_s\":20,\"sleep_between\":true,\"fix_timeout_s\":120}}",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsentOverrideOmitsTheKeyRatherThanWritingNull()
    {
        string json = JsonSerializer.Serialize(
            Sample(withOverride: false), DeviceScheduleBundleDto.SerializerOptions);

        // The firmware tests for the key's PRESENCE. "override": null would be decoded
        // as an override with no expiry and no values, which would pin the device to
        // whatever it last had — the opposite of what an absent override means.
        Assert.DoesNotContain("override", json);
    }

    [Fact]
    public void AnOverrideCarriesItsInstantInTheOnlyFormatTheFirmwareParses()
    {
        string json = JsonSerializer.Serialize(
            Sample(withOverride: true), DeviceScheduleBundleDto.SerializerOptions);

        // CivilTime::parseIso accepts exactly these twenty characters and rejects
        // everything else: no fractional seconds, no offset, no lower-case z. A
        // DateTime serialized the ordinary way would carry a fractional part and every
        // override would be silently ignored.
        Assert.Contains("\"until\":\"2026-09-06T22:00:00Z\"", json);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void FormatDeviceInstantIsStableAcrossTheKindsThisApplicationProduces(DateTimeKind kind)
    {
        // Npgsql hands back Utc for a timestamptz, but a DateTime that has been through
        // a projection can arrive Unspecified. Every timestamp in this application is
        // UTC, so both must render identically — a silent local-time conversion here
        // would expire overrides hours early or late.
        DateTime instant = DateTime.SpecifyKind(
            new DateTime(2026, 1, 2, 3, 4, 5), kind);

        Assert.Equal("2026-01-02T03:04:05Z", ScheduleBundleBuilder.FormatDeviceInstant(instant));
    }

    [Fact]
    public void NoFallbackIsTheNegativeSentinelRatherThanNull()
    {
        DeviceScheduleBundleDto bundle = new DeviceScheduleBundleDto(
            1,
            false,
            DeviceScheduleBundleDto.NoFallbackSlot,
            new List<ScheduleBundleProfileDto>(),
            new List<ScheduleBundleRuleDto>(),
            null);

        string json = JsonSerializer.Serialize(bundle, DeviceScheduleBundleDto.SerializerOptions);

        // A sentinel keeps the device reading one integer instead of inspecting a JSON
        // null, which is a branch its parser would otherwise have to get right.
        Assert.Contains("\"fallback\":-1", json);
    }
}
