using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="DeviceConfigVersion"/>. Column names are spelled out in
/// snake_case explicitly, matching the house style set by
/// <see cref="DeviceConfiguration"/> and <see cref="PositionConfiguration"/>.
///
/// <para>
/// Every numeric column carries a CHECK constraint built from
/// <see cref="DeviceConfigRules"/>. The DTO's <c>[Range]</c> attributes already reject
/// the same values with a 400, so these are a second line of defence rather than the
/// primary one — they are what stops a hand-written <c>UPDATE</c> during maintenance
/// from publishing a document that would leave a fleet clamping silently.
/// </para>
/// </summary>
public sealed class DeviceConfigVersionConfiguration : IEntityTypeConfiguration<DeviceConfigVersion>
{
    /// <summary>Configures the device_config_versions table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DeviceConfigVersion> builder)
    {
        builder.ToTable(
            "device_config_versions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_device_config_versions_interval_s",
                    $"interval_s BETWEEN {DeviceConfigRules.MinIntervalSeconds} AND {DeviceConfigRules.MaxIntervalSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_fix_timeout_s",
                    $"fix_timeout_s BETWEEN {DeviceConfigRules.MinFixTimeoutSeconds} AND {DeviceConfigRules.MaxFixTimeoutSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_queue_max_fixes",
                    $"queue_max_fixes BETWEEN {DeviceConfigRules.MinQueueMaxFixes} AND {DeviceConfigRules.MaxQueueMaxFixes}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_retry_interval_h",
                    $"retry_interval_h BETWEEN {DeviceConfigRules.MinRetryIntervalHours} AND {DeviceConfigRules.MaxRetryIntervalHours}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_retry_max_age_h",
                    $"retry_max_age_h BETWEEN {DeviceConfigRules.MinRetryMaxAgeHours} AND {DeviceConfigRules.MaxRetryMaxAgeHours}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_config_check_s",
                    $"config_check_s BETWEEN {DeviceConfigRules.MinConfigCheckSeconds} AND {DeviceConfigRules.MaxConfigCheckSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_motion_threshold_mg",
                    $"motion_threshold_mg BETWEEN {DeviceConfigRules.MinMotionThresholdMg} AND {DeviceConfigRules.MaxMotionThresholdMg}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_motion_speed_kmph",
                    $"motion_speed_kmph BETWEEN {DeviceConfigRules.MinMotionSpeedKmph} AND {DeviceConfigRules.MaxMotionSpeedKmph}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_motion_wake_wait_s",
                    $"motion_wake_wait_s BETWEEN {DeviceConfigRules.MinMotionWakeWaitSeconds} AND {DeviceConfigRules.MaxMotionWakeWaitSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_motion_stop_wait_s",
                    $"motion_stop_wait_s BETWEEN {DeviceConfigRules.MinMotionStopWaitSeconds} AND {DeviceConfigRules.MaxMotionStopWaitSeconds}");

                // The moving set is held to the standby set's bounds: a setting means
                // the same thing in either mode, so there is nothing to bound twice.
                table.HasCheckConstraint(
                    "ck_device_config_versions_moving_interval_s",
                    $"moving_interval_s BETWEEN {DeviceConfigRules.MinIntervalSeconds} AND {DeviceConfigRules.MaxIntervalSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_moving_fix_timeout_s",
                    $"moving_fix_timeout_s BETWEEN {DeviceConfigRules.MinFixTimeoutSeconds} AND {DeviceConfigRules.MaxFixTimeoutSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_versions_version",
                    $"version >= {DeviceConfigRules.InitialVersion}");
            });

        builder.HasKey(configVersion => configVersion.Id);

        builder.Property(configVersion => configVersion.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(configVersion => configVersion.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        builder.Property(configVersion => configVersion.Version)
            .HasColumnName("version")
            .IsRequired();

        // The pair a device's history is addressed by: resolving "which values is it
        // running?" is a lookup on exactly this, and the uniqueness is what guarantees
        // a version number identifies one document rather than several.
        builder.HasIndex(configVersion => new { configVersion.DeviceId, configVersion.Version })
            .IsUnique()
            .HasDatabaseName("ux_device_config_versions_device_id_version");

        builder.Property(configVersion => configVersion.IntervalSeconds)
            .HasColumnName("interval_s")
            .IsRequired();

        builder.Property(configVersion => configVersion.SleepBetween)
            .HasColumnName("sleep_between")
            .IsRequired();

        builder.Property(configVersion => configVersion.FixTimeoutSeconds)
            .HasColumnName("fix_timeout_s")
            .IsRequired();

        builder.Property(configVersion => configVersion.QueueMaxFixes)
            .HasColumnName("queue_max_fixes")
            .IsRequired();

        builder.Property(configVersion => configVersion.RetryIntervalHours)
            .HasColumnName("retry_interval_h")
            .IsRequired();

        builder.Property(configVersion => configVersion.RetryMaxAgeHours)
            .HasColumnName("retry_max_age_h")
            .IsRequired();

        builder.Property(configVersion => configVersion.ConfigCheckSeconds)
            .HasColumnName("config_check_s")
            .HasDefaultValue(DeviceConfigRules.DefaultConfigCheckSeconds)
            .IsRequired();

        // The motion block. Every column carries a default so the migration backfills
        // existing revisions with the same values a new device gets, and so a
        // hand-written INSERT that predates the feature still produces a valid revision.
        // The Moving* defaults are the firmware's, not copies of the standby ones: the
        // whole point of the second set is that it differs. Only interval, sleep and fix
        // timeout have a moving copy; the queue, retry and re-check columns above serve
        // both modes.
        // The sentinel is set explicitly because the default is TRUE. EF leaves a column
        // out of an INSERT when its value equals the property's sentinel, so that the
        // database default can apply — and the sentinel is the CLR default (false) unless
        // configured otherwise. A revision saved with motion wake switched OFF would then
        // be stored as on: the dashboard says off, the device is told on, and no error
        // appears anywhere. Pinning the sentinel to the default means only a value that
        // equals the default is ever left to the database.
        builder.Property(configVersion => configVersion.MotionEnabled)
            .HasColumnName("motion_enabled")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionEnabled)
            .HasSentinel(DeviceConfigRules.DefaultMotionEnabled)
            .IsRequired();

        builder.Property(configVersion => configVersion.MotionThresholdMg)
            .HasColumnName("motion_threshold_mg")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionThresholdMg)
            .IsRequired();

        builder.Property(configVersion => configVersion.MotionSpeedKmph)
            .HasColumnName("motion_speed_kmph")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionSpeedKmph)
            .IsRequired();

        builder.Property(configVersion => configVersion.MotionWakeWaitSeconds)
            .HasColumnName("motion_wake_wait_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionWakeWaitSeconds)
            .IsRequired();

        builder.Property(configVersion => configVersion.MotionStopWaitSeconds)
            .HasColumnName("motion_stop_wait_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionStopWaitSeconds)
            .IsRequired();

        builder.Property(configVersion => configVersion.MovingIntervalSeconds)
            .HasColumnName("moving_interval_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingIntervalSeconds)
            .IsRequired();

        builder.Property(configVersion => configVersion.MovingSleepBetween)
            .HasColumnName("moving_sleep_between")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingSleepBetween)
            .IsRequired();

        builder.Property(configVersion => configVersion.MovingFixTimeoutSeconds)
            .HasColumnName("moving_fix_timeout_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingFixTimeoutSeconds)
            .IsRequired();

        builder.Property(configVersion => configVersion.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(configVersion => configVersion.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        // Stored as the enum's int value. Manual is 0, which is what every row that
        // predates schedules already means — so the migration needs no backfill and no
        // guess about rows it cannot know the provenance of.
        builder.Property(configVersion => configVersion.Source)
            .HasColumnName("source")
            .HasConversion<int>()
            .HasDefaultValue(ConfigRevisionSource.Manual)
            .IsRequired();

        builder.Property(configVersion => configVersion.SourceProfileId)
            .HasColumnName("source_profile_id");

        // Cascade: the history of a device that is genuinely gone from the table has
        // nothing left to describe. Note this is not the normal retirement path —
        // deleting a device is a soft delete, which leaves these rows untouched.
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(configVersion => configVersion.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not cascade: an account being removed must never take a device's
        // configuration history with it. The author becoming unknown is acceptable;
        // the revision disappearing is not.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(configVersion => configVersion.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull for the originating profile, for the same reason one step further on:
        // the row already holds the values in full, so losing the profile costs the
        // history a label, not a fact. Restricting instead would mean a profile could
        // never be deleted once the scheduler had used it even once.
        builder.HasOne<DeviceConfigProfile>()
            .WithMany()
            .HasForeignKey(configVersion => configVersion.SourceProfileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
