using System.Text.Json;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Devices;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the wire format of the retained settings document, <c>devices/&lt;id&gt;/config</c>.
///
/// <para>
/// Like the schedule bundle, this shape is owned by the firmware: <c>SettingsCodec</c>
/// is the only decoder, for the MQTT message and the copy cached on the SD card alike.
/// A renamed or misplaced key does not raise an error anywhere a person would see it —
/// the device clamps or defaults what it cannot read and carries on, looking healthy.
/// With the motion block that failure is worse than usual: a tracker whose
/// <c>motion.enabled</c> was dropped on the floor simply never wakes on movement.
/// </para>
/// </summary>
public class DeviceConfigDocumentSerializationTests
{
    /// <summary>
    /// A document with every value distinct from every other, so a field mapped to the
    /// wrong key shows up as a wrong number rather than hiding behind a repeated one.
    /// </summary>
    private static DeviceConfigDocumentDto Sample()
    {
        return new DeviceConfigDocumentDto(
            7,
            60,
            true,
            180,
            20000,
            24,
            168,
            3600,
            new DeviceMotionDocumentDto(
                true,
                63,
                3,
                240,
                600,
                new DeviceModeDocumentDto(10, false, 150, 25000, 12, 96, 1800)));
    }

    [Fact]
    public void TheMotionBlockFollowsConfigCheckWithTheExactKeysAndNesting()
    {
        // The publisher serializes with the default options (no naming policy), so the
        // explicit JsonPropertyName attributes are the only thing deciding the spelling.
        string json = JsonSerializer.Serialize(Sample());

        // One string, order included: "motion" is the last member and "moving" the last
        // member of "motion", exactly as the firmware's example documents show it.
        Assert.Contains(
            "\"config_check_s\":3600,\"motion\":{"
                + "\"enabled\":true,\"threshold_mg\":63,\"speed_kmph\":3,"
                + "\"wake_wait_s\":240,\"stop_wait_s\":600,"
                + "\"moving\":{\"interval_s\":10,\"sleep_between\":false,\"fix_timeout_s\":150,"
                + "\"queue_max_fixes\":25000,\"retry_interval_h\":12,\"retry_max_age_h\":96,"
                + "\"config_check_s\":1800}}}",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheStandbyKeysStayAtTheTopLevelUnchanged()
    {
        string json = JsonSerializer.Serialize(Sample());

        // The seven existing keys are the STANDBY set and must not move: a device on
        // firmware that predates the motion block reads exactly these and ignores the
        // rest, which is the whole backward-compatibility story of the feature.
        Assert.StartsWith(
            "{\"version\":7,\"interval_s\":60,\"sleep_between\":true,\"fix_timeout_s\":180,"
                + "\"queue_max_fixes\":20000,\"retry_interval_h\":24,\"retry_max_age_h\":168,"
                + "\"config_check_s\":3600,\"motion\":{",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheMovingObjectCarriesNoVersionKey()
    {
        string json = JsonSerializer.Serialize(Sample());

        // "version" belongs to the document, not to a mode: the device echoes one
        // number per report, and a second one inside "moving" would have nothing to
        // mean. It appears exactly once, at the top.
        Assert.Equal(1, CountOccurrences(json, "\"version\""));
    }

    [Fact]
    public void ARevisionMapsEachColumnToItsOwnKey()
    {
        // The factory is twelve values of mostly the same type in a row, which is
        // exactly the shape in which two get swapped without anything failing to
        // compile. Every column here holds a different number for that reason.
        DeviceConfigVersion revision = new DeviceConfigVersion
        {
            MotionEnabled = true,
            MotionThresholdMg = 101,
            MotionSpeedKmph = 102,
            MotionWakeWaitSeconds = 103,
            MotionStopWaitSeconds = 104,
            MovingIntervalSeconds = 105,
            MovingSleepBetween = true,
            MovingFixTimeoutSeconds = 106,
            MovingQueueMaxFixes = 107,
            MovingRetryIntervalHours = 108,
            MovingRetryMaxAgeHours = 109,
            MovingConfigCheckSeconds = 110,
        };

        string json = JsonSerializer.Serialize(DeviceMotionDocumentFactory.Create(revision));

        Assert.Equal(
            "{\"enabled\":true,\"threshold_mg\":101,\"speed_kmph\":102,\"wake_wait_s\":103,"
                + "\"stop_wait_s\":104,\"moving\":{\"interval_s\":105,\"sleep_between\":true,"
                + "\"fix_timeout_s\":106,\"queue_max_fixes\":107,\"retry_interval_h\":108,"
                + "\"retry_max_age_h\":109,\"config_check_s\":110}}",
            json);
    }

    [Fact]
    public void AllThreeSourcesOfTheMotionBlockAgree()
    {
        // The revision, the profile and the dashboard's flat shape describe the same
        // twelve settings, and the publishers pick whichever one they happen to have.
        // If the three overloads ever disagreed, the same settings would reach the
        // device differently depending on which code path delivered them.
        DeviceConfigVersion revision = new DeviceConfigVersion
        {
            MotionEnabled = true,
            MotionThresholdMg = 201,
            MotionSpeedKmph = 202,
            MotionWakeWaitSeconds = 203,
            MotionStopWaitSeconds = 204,
            MovingIntervalSeconds = 205,
            MovingSleepBetween = true,
            MovingFixTimeoutSeconds = 206,
            MovingQueueMaxFixes = 207,
            MovingRetryIntervalHours = 208,
            MovingRetryMaxAgeHours = 209,
            MovingConfigCheckSeconds = 210,
        };

        DeviceConfigProfile profile = new DeviceConfigProfile
        {
            MotionEnabled = revision.MotionEnabled,
            MotionThresholdMg = revision.MotionThresholdMg,
            MotionSpeedKmph = revision.MotionSpeedKmph,
            MotionWakeWaitSeconds = revision.MotionWakeWaitSeconds,
            MotionStopWaitSeconds = revision.MotionStopWaitSeconds,
            MovingIntervalSeconds = revision.MovingIntervalSeconds,
            MovingSleepBetween = revision.MovingSleepBetween,
            MovingFixTimeoutSeconds = revision.MovingFixTimeoutSeconds,
            MovingQueueMaxFixes = revision.MovingQueueMaxFixes,
            MovingRetryIntervalHours = revision.MovingRetryIntervalHours,
            MovingRetryMaxAgeHours = revision.MovingRetryMaxAgeHours,
            MovingConfigCheckSeconds = revision.MovingConfigCheckSeconds,
        };

        DeviceConfigValuesDto values = new DeviceConfigValuesDto(
            60,
            false,
            180,
            20000,
            24,
            168,
            3600,
            revision.MotionEnabled,
            revision.MotionThresholdMg,
            revision.MotionSpeedKmph,
            revision.MotionWakeWaitSeconds,
            revision.MotionStopWaitSeconds,
            revision.MovingIntervalSeconds,
            revision.MovingSleepBetween,
            revision.MovingFixTimeoutSeconds,
            revision.MovingQueueMaxFixes,
            revision.MovingRetryIntervalHours,
            revision.MovingRetryMaxAgeHours,
            revision.MovingConfigCheckSeconds);

        DeviceMotionDocumentDto fromRevision = DeviceMotionDocumentFactory.Create(revision);

        Assert.Equal(fromRevision, DeviceMotionDocumentFactory.Create(profile));
        Assert.Equal(fromRevision, DeviceMotionDocumentFactory.Create(values));
    }

    /// <summary>Counts non-overlapping occurrences of a substring.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="needle">What to count.</param>
    /// <returns>How many times <paramref name="needle"/> occurs.</returns>
    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = text.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
