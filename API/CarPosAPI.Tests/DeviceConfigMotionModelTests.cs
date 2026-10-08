using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Devices;
using CarPosAPI.Services.Ingest;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards two things about how the motion columns meet EF Core that compile cleanly
/// and then fail without a sound: a chosen value being replaced by a column default on
/// insert, and the fleet-wide republish query silently failing to translate.
///
/// <para>
/// Neither needs a database. The model is inspected directly, and, as in
/// <see cref="DeviceAccessCountsQueryTranslationTests"/>, <c>ToQueryString()</c> runs
/// the query through the real Npgsql provider without ever opening a connection.
/// </para>
/// </summary>
public sealed class DeviceConfigMotionModelTests
{
    /// <summary>Never connected to — the provider only needs it to be well-formed.</summary>
    private const string UnusedConnectionString =
        "Host=localhost;Database=carpos_model_check;Username=none;Password=none";

    private static CarPosDbContext CreateContext()
    {
        DbContextOptions<CarPosDbContext> options = new DbContextOptionsBuilder<CarPosDbContext>()
            .UseNpgsql(UnusedConnectionString)
            .Options;

        return new CarPosDbContext(options);
    }

    [Theory]
    [InlineData(typeof(DeviceConfigVersion))]
    [InlineData(typeof(DeviceConfigProfile))]
    public void AMovingGiveUpAgeOfZeroIsNotReplacedByTheDatabaseDefault(Type entityType)
    {
        using CarPosDbContext context = CreateContext();

        IProperty property = context.Model
            .FindEntityType(entityType)!
            .FindProperty(nameof(DeviceConfigVersion.MovingRetryMaxAgeHours))!;

        // EF leaves a column out of an INSERT when the value equals the property's
        // sentinel, so that the database default can apply. The sentinel is the CLR
        // default (0) unless configured otherwise — and for this column 0 is a real,
        // chosen value meaning "never give up on a rejected fix", while the default is
        // 168. With the usual sentinel a revision saved with 0 would be stored as a
        // week, with no error anywhere and a dashboard that still says "never".
        Assert.Equal(DeviceConfigRules.DefaultMovingRetryMaxAgeHours, property.GetDefaultValue());
        Assert.NotEqual(
            0,
            Assert.IsType<int>(property.Sentinel));
    }

    [Fact]
    public void TheFleetRepublishQueryTranslatesAndCarriesTheMotionColumns()
    {
        using CarPosDbContext context = CreateContext();

        // The shape MqttConfigPublisher.RepublishAllAsync and DeviceConfigService
        // .RepublishAsync both use: join each device to the revision it points at and
        // project straight to the document. The motion block is built by calling the
        // factory inside that projection. EF will run such a call client-side on the
        // materialised entity — which is fine — but if it ever stopped being able to,
        // the failure would surface inside a catch-all on the broker-connect path,
        // logged as a warning, with every retained document left stale.
        IQueryable<DeviceConfigPublication> query = context.Devices
            .AsNoTracking()
            .Where(device => device.IsActive)
            .Join(
                context.DeviceConfigVersions.AsNoTracking(),
                device => new { DeviceRowId = device.Id, Version = device.ConfigVersion },
                configVersion => new { DeviceRowId = configVersion.DeviceId, configVersion.Version },
                (device, configVersion) => new DeviceConfigPublication(
                    device.DeviceId,
                    new DeviceConfigDocumentDto(
                        configVersion.Version,
                        configVersion.IntervalSeconds,
                        configVersion.SleepBetween,
                        configVersion.FixTimeoutSeconds,
                        configVersion.QueueMaxFixes,
                        configVersion.RetryIntervalHours,
                        configVersion.RetryMaxAgeHours,
                        configVersion.ConfigCheckSeconds,
                        DeviceMotionDocumentFactory.Create(configVersion))));

        string sql = query.ToQueryString();

        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);

        foreach (string column in new[]
        {
            "motion_enabled",
            "motion_threshold_mg",
            "motion_speed_kmph",
            "motion_wake_wait_s",
            "motion_stop_wait_s",
            "moving_interval_s",
            "moving_sleep_between",
            "moving_fix_timeout_s",
            "moving_queue_max_fixes",
            "moving_retry_interval_h",
            "moving_retry_max_age_h",
            "moving_config_check_s",
        })
        {
            Assert.Contains(column, sql, StringComparison.Ordinal);
        }
    }
}
