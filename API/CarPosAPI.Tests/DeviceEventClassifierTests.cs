using CarPosAPI.Dtos;
using CarPosAPI.Services.Ingest;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins down the one table that decides what each device report means and how much it
/// matters. Changing a row here changes what every dashboard shows for every device, so
/// it should take a deliberate edit to this file as well.
/// </summary>
public sealed class DeviceEventClassifierTests
{
    [Theory]
    [InlineData("sleep", DeviceEventReasonNames.Sleep, DeviceEventSeverityNames.Normal)]
    [InlineData("sleep_no_motion", DeviceEventReasonNames.SleepNoMotion, DeviceEventSeverityNames.Normal)]
    [InlineData("power_off", DeviceEventReasonNames.PowerOff, DeviceEventSeverityNames.Normal)]
    [InlineData("battery_low", DeviceEventReasonNames.BatteryLow, DeviceEventSeverityNames.Alert)]
    [InlineData("error", DeviceEventReasonNames.Error, DeviceEventSeverityNames.Error)]
    [InlineData("connection_lost", DeviceEventReasonNames.ConnectionLost, DeviceEventSeverityNames.Error)]
    public void ClassifiesEveryOfflineReasonTheFirmwareSends(
        string deviceReason,
        string expectedReason,
        string expectedSeverity)
    {
        bool known = DeviceEventClassifier.TryClassifyOffline(deviceReason, out DeviceEventClassification? classification);

        Assert.True(known);
        Assert.NotNull(classification);
        Assert.Equal(DeviceEventKindNames.Offline, classification.Kind);
        Assert.Equal(expectedReason, classification.Reason);
        Assert.Equal(expectedSeverity, classification.Severity);
    }

    [Fact]
    public void RefusesAnUnknownOfflineReason()
    {
        bool known = DeviceEventClassifier.TryClassifyOffline("napping", out DeviceEventClassification? classification);

        Assert.False(known);
        Assert.Null(classification);
    }

    [Theory]
    [InlineData("POWERON", DeviceEventReasonNames.PowerOn, DeviceEventSeverityNames.Normal)]
    [InlineData("BROWNOUT", DeviceEventReasonNames.PowerLoss, DeviceEventSeverityNames.Alert)]
    [InlineData("PANIC", DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error)]
    [InlineData("INT_WDT", DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error)]
    [InlineData("TASK_WDT", DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error)]
    [InlineData("WDT", DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error)]
    [InlineData("SW", DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error)]
    public void ClassifiesTheResetsWorthARow(string resetReason, string expectedReason, string expectedSeverity)
    {
        DeviceEventClassification? classification = DeviceEventClassifier.ClassifyRestart(resetReason);

        Assert.NotNull(classification);
        Assert.Equal(DeviceEventKindNames.Restart, classification.Kind);
        Assert.Equal(expectedReason, classification.Reason);
        Assert.Equal(expectedSeverity, classification.Severity);
    }

    [Theory]
    // Every ordinary deep-sleep wake — would otherwise be a row per report.
    [InlineData("DEEPSLEEP")]
    [InlineData("EXT")]
    [InlineData("SDIO")]
    [InlineData("UNKNOWN")]
    [InlineData(null)]
    public void RecordsNoRowForRoutineResets(string? resetReason)
    {
        Assert.Null(DeviceEventClassifier.ClassifyRestart(resetReason));
    }

    [Theory]
    [InlineData("timer", DeviceEventReasonNames.Timer)]
    [InlineData("accelerometer", DeviceEventReasonNames.Accelerometer)]
    [InlineData("power_switch", DeviceEventReasonNames.PowerSwitch)]
    public void ClassifiesEveryWakeCauseTheFirmwareSends(string deviceReason, string expectedReason)
    {
        bool known = DeviceEventClassifier.TryClassifyWake(deviceReason, out DeviceEventClassification? classification);

        Assert.True(known);
        Assert.NotNull(classification);
        Assert.Equal(DeviceEventKindNames.Wake, classification.Kind);
        Assert.Equal(expectedReason, classification.Reason);
        Assert.Equal(DeviceEventSeverityNames.Normal, classification.Severity);
    }

    [Theory]
    [InlineData("checking", DeviceEventReasonNames.Checking)]
    [InlineData("activity", DeviceEventReasonNames.Activity)]
    [InlineData("motion_on", DeviceEventReasonNames.MotionOn)]
    [InlineData("moving", DeviceEventReasonNames.Moving)]
    [InlineData("no_motion", DeviceEventReasonNames.NoMotion)]
    [InlineData("stopped", DeviceEventReasonNames.Stopped)]
    [InlineData("motion_off", DeviceEventReasonNames.MotionOff)]
    public void ClassifiesEveryMotionStepTheFirmwareSends(string deviceReason, string expectedReason)
    {
        bool known = DeviceEventClassifier.TryClassifyMotion(deviceReason, out DeviceEventClassification? classification);

        Assert.True(known);
        Assert.NotNull(classification);
        Assert.Equal(DeviceEventKindNames.Motion, classification.Kind);
        Assert.Equal(expectedReason, classification.Reason);
        Assert.Equal(DeviceEventSeverityNames.Normal, classification.Severity);
    }

    [Fact]
    public void KeepsEachVocabularyToItsOwnType()
    {
        // A word is only meaningful under the type that sends it: a "timer" offline or a
        // "sleep" motion step is not something the firmware says.
        Assert.False(DeviceEventClassifier.TryClassifyOffline("timer", out _));
        Assert.False(DeviceEventClassifier.TryClassifyWake("sleep", out _));
        Assert.False(DeviceEventClassifier.TryClassifyMotion("accelerometer", out _));
    }

    [Fact]
    public void EveryStoredValueSatisfiesTheDatabaseConstraints()
    {
        // The CHECK constraints in DeviceEventConfiguration list these vocabularies by
        // hand. A classification outside them would be dropped as poison on insert, so
        // the two lists have to agree.
        string[] offlineReasons =
        [
            DeviceEventReasonNames.Sleep,
            DeviceEventReasonNames.SleepNoMotion,
            DeviceEventReasonNames.PowerOff,
            DeviceEventReasonNames.BatteryLow,
            DeviceEventReasonNames.Error,
            DeviceEventReasonNames.ConnectionLost,
        ];
        string[] restartReasons =
        [
            DeviceEventReasonNames.PowerOn,
            DeviceEventReasonNames.PowerLoss,
            DeviceEventReasonNames.Crash,
        ];
        string[] wakeReasons =
        [
            DeviceEventReasonNames.Timer,
            DeviceEventReasonNames.Accelerometer,
            DeviceEventReasonNames.PowerSwitch,
        ];
        string[] motionReasons =
        [
            DeviceEventReasonNames.Checking,
            DeviceEventReasonNames.Activity,
            DeviceEventReasonNames.MotionOn,
            DeviceEventReasonNames.Moving,
            DeviceEventReasonNames.NoMotion,
            DeviceEventReasonNames.Stopped,
            DeviceEventReasonNames.MotionOff,
        ];
        string[] severities = DeviceEventSeverityNames.AtLeast(null).ToArray();

        foreach (string deviceReason in new[] { "sleep", "sleep_no_motion", "power_off", "battery_low", "error", "connection_lost" })
        {
            DeviceEventClassifier.TryClassifyOffline(deviceReason, out DeviceEventClassification? classification);
            Assert.Contains(classification!.Reason, offlineReasons);
            Assert.Contains(classification.Severity, severities);
        }

        foreach (string resetReason in new[] { "POWERON", "BROWNOUT", "PANIC", "INT_WDT", "TASK_WDT", "WDT", "SW" })
        {
            DeviceEventClassification classification = DeviceEventClassifier.ClassifyRestart(resetReason)!;
            Assert.Contains(classification.Reason, restartReasons);
            Assert.Contains(classification.Severity, severities);
        }

        foreach (string wakeCause in new[] { "timer", "accelerometer", "power_switch" })
        {
            DeviceEventClassifier.TryClassifyWake(wakeCause, out DeviceEventClassification? classification);
            Assert.Contains(classification!.Reason, wakeReasons);
            Assert.Contains(classification.Severity, severities);
        }

        foreach (string motionStep in new[] { "checking", "activity", "motion_on", "moving", "no_motion", "stopped", "motion_off" })
        {
            DeviceEventClassifier.TryClassifyMotion(motionStep, out DeviceEventClassification? classification);
            Assert.Contains(classification!.Reason, motionReasons);
            Assert.Contains(classification.Severity, severities);
        }
    }

    [Theory]
    [InlineData(null, new[] { "normal", "alert", "error" })]
    [InlineData("normal", new[] { "normal", "alert", "error" })]
    [InlineData("alert", new[] { "alert", "error" })]
    [InlineData("error", new[] { "error" })]
    public void SeverityFilterIncludesThisAndWorse(string? minimum, string[] expected)
    {
        Assert.Equal(expected, DeviceEventSeverityNames.AtLeast(minimum));
    }
}
