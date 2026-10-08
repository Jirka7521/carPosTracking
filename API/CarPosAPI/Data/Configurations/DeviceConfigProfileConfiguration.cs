using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="DeviceConfigProfile"/>, following
/// <see cref="DeviceConfigVersionConfiguration"/> exactly — snake_case column names
/// spelled out, and a CHECK constraint on every numeric column built from
/// <see cref="DeviceConfigRules"/>.
///
/// <para>
/// The constraints matter more here than they do on a revision. A revision is written
/// once by code that has already validated it; a profile is edited repeatedly and its
/// values are copied into a revision <em>by a background worker</em>, with no request
/// and no <c>[Range]</c> attribute anywhere in the path. These constraints are what
/// guarantee the worker cannot publish a document the firmware would silently clamp.
/// </para>
/// </summary>
public sealed class DeviceConfigProfileConfiguration : IEntityTypeConfiguration<DeviceConfigProfile>
{
    /// <summary>Configures the device_config_profiles table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DeviceConfigProfile> builder)
    {
        builder.ToTable(
            "device_config_profiles",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_device_config_profiles_interval_s",
                    $"interval_s BETWEEN {DeviceConfigRules.MinIntervalSeconds} AND {DeviceConfigRules.MaxIntervalSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_fix_timeout_s",
                    $"fix_timeout_s BETWEEN {DeviceConfigRules.MinFixTimeoutSeconds} AND {DeviceConfigRules.MaxFixTimeoutSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_queue_max_fixes",
                    $"queue_max_fixes BETWEEN {DeviceConfigRules.MinQueueMaxFixes} AND {DeviceConfigRules.MaxQueueMaxFixes}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_retry_interval_h",
                    $"retry_interval_h BETWEEN {DeviceConfigRules.MinRetryIntervalHours} AND {DeviceConfigRules.MaxRetryIntervalHours}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_retry_max_age_h",
                    $"retry_max_age_h BETWEEN {DeviceConfigRules.MinRetryMaxAgeHours} AND {DeviceConfigRules.MaxRetryMaxAgeHours}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_config_check_s",
                    $"config_check_s BETWEEN {DeviceConfigRules.MinConfigCheckSeconds} AND {DeviceConfigRules.MaxConfigCheckSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_motion_threshold_mg",
                    $"motion_threshold_mg BETWEEN {DeviceConfigRules.MinMotionThresholdMg} AND {DeviceConfigRules.MaxMotionThresholdMg}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_motion_speed_kmph",
                    $"motion_speed_kmph BETWEEN {DeviceConfigRules.MinMotionSpeedKmph} AND {DeviceConfigRules.MaxMotionSpeedKmph}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_motion_wake_wait_s",
                    $"motion_wake_wait_s BETWEEN {DeviceConfigRules.MinMotionWakeWaitSeconds} AND {DeviceConfigRules.MaxMotionWakeWaitSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_motion_stop_wait_s",
                    $"motion_stop_wait_s BETWEEN {DeviceConfigRules.MinMotionStopWaitSeconds} AND {DeviceConfigRules.MaxMotionStopWaitSeconds}");

                // The moving set is held to the standby set's bounds: a setting means
                // the same thing in either mode, so there is nothing to bound twice.
                table.HasCheckConstraint(
                    "ck_device_config_profiles_moving_interval_s",
                    $"moving_interval_s BETWEEN {DeviceConfigRules.MinIntervalSeconds} AND {DeviceConfigRules.MaxIntervalSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_moving_fix_timeout_s",
                    $"moving_fix_timeout_s BETWEEN {DeviceConfigRules.MinFixTimeoutSeconds} AND {DeviceConfigRules.MaxFixTimeoutSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_moving_queue_max_fixes",
                    $"moving_queue_max_fixes BETWEEN {DeviceConfigRules.MinQueueMaxFixes} AND {DeviceConfigRules.MaxQueueMaxFixes}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_moving_retry_interval_h",
                    $"moving_retry_interval_h BETWEEN {DeviceConfigRules.MinRetryIntervalHours} AND {DeviceConfigRules.MaxRetryIntervalHours}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_moving_retry_max_age_h",
                    $"moving_retry_max_age_h BETWEEN {DeviceConfigRules.MinRetryMaxAgeHours} AND {DeviceConfigRules.MaxRetryMaxAgeHours}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_moving_config_check_s",
                    $"moving_config_check_s BETWEEN {DeviceConfigRules.MinConfigCheckSeconds} AND {DeviceConfigRules.MaxConfigCheckSeconds}");
                table.HasCheckConstraint(
                    "ck_device_config_profiles_schedule_slot",
                    $"schedule_slot BETWEEN {ScheduleRules.MinScheduleSlot} AND {ScheduleRules.MaxScheduleSlot}");
            });

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(profile => profile.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        builder.Property(profile => profile.Name)
            .HasColumnName("name")
            .HasMaxLength(ScheduleRules.MaxProfileNameLength)
            .IsRequired();

        // Exact-name uniqueness per device, as a backstop. The user-facing rule is
        // stricter — DeviceConfigScheduleService rejects a name that differs only by
        // case, so "Night" and "night" cannot coexist and a rule list stays readable.
        // That check gets to answer 409 with a sentence; this index only guarantees
        // that a race between two saves cannot slip a genuine duplicate past it.
        builder.HasIndex(profile => new { profile.DeviceId, profile.Name })
            .IsUnique()
            .HasDatabaseName("ux_device_config_profiles_device_id_name");

        builder.Property(profile => profile.ScheduleSlot)
            .HasColumnName("schedule_slot")
            .IsRequired();

        // The firmware addresses profiles by slot, so two profiles sharing one would
        // not be a cosmetic problem — it would make a device's report ambiguous about
        // which settings it is actually running. Unique per device, and the CHECK
        // above keeps every value inside the range the bundle can express.
        builder.HasIndex(profile => new { profile.DeviceId, profile.ScheduleSlot })
            .IsUnique()
            .HasDatabaseName("ux_device_config_profiles_device_id_schedule_slot");

        builder.Property(profile => profile.IntervalSeconds)
            .HasColumnName("interval_s")
            .IsRequired();

        builder.Property(profile => profile.SleepBetween)
            .HasColumnName("sleep_between")
            .IsRequired();

        builder.Property(profile => profile.FixTimeoutSeconds)
            .HasColumnName("fix_timeout_s")
            .IsRequired();

        builder.Property(profile => profile.QueueMaxFixes)
            .HasColumnName("queue_max_fixes")
            .IsRequired();

        builder.Property(profile => profile.RetryIntervalHours)
            .HasColumnName("retry_interval_h")
            .IsRequired();

        builder.Property(profile => profile.RetryMaxAgeHours)
            .HasColumnName("retry_max_age_h")
            .IsRequired();

        builder.Property(profile => profile.ConfigCheckSeconds)
            .HasColumnName("config_check_s")
            .IsRequired();

        // The motion block. Unlike the seven columns above, every one of these carries
        // a default: they were added to a table that already held profiles, and the
        // migration needs a value to backfill them with that leaves every existing
        // profile behaving exactly as it did (motion off). The Moving* defaults are the
        // firmware's, not copies of the standby ones.
        builder.Property(profile => profile.MotionEnabled)
            .HasColumnName("motion_enabled")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionEnabled)
            .IsRequired();

        builder.Property(profile => profile.MotionThresholdMg)
            .HasColumnName("motion_threshold_mg")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionThresholdMg)
            .IsRequired();

        builder.Property(profile => profile.MotionSpeedKmph)
            .HasColumnName("motion_speed_kmph")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionSpeedKmph)
            .IsRequired();

        builder.Property(profile => profile.MotionWakeWaitSeconds)
            .HasColumnName("motion_wake_wait_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionWakeWaitSeconds)
            .IsRequired();

        builder.Property(profile => profile.MotionStopWaitSeconds)
            .HasColumnName("motion_stop_wait_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMotionStopWaitSeconds)
            .IsRequired();

        builder.Property(profile => profile.MovingIntervalSeconds)
            .HasColumnName("moving_interval_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingIntervalSeconds)
            .IsRequired();

        builder.Property(profile => profile.MovingSleepBetween)
            .HasColumnName("moving_sleep_between")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingSleepBetween)
            .IsRequired();

        builder.Property(profile => profile.MovingFixTimeoutSeconds)
            .HasColumnName("moving_fix_timeout_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingFixTimeoutSeconds)
            .IsRequired();

        builder.Property(profile => profile.MovingQueueMaxFixes)
            .HasColumnName("moving_queue_max_fixes")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingQueueMaxFixes)
            .IsRequired();

        builder.Property(profile => profile.MovingRetryIntervalHours)
            .HasColumnName("moving_retry_interval_h")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingRetryIntervalHours)
            .IsRequired();

        // The sentinel is set explicitly, and for this column only: 0 is a real, chosen
        // value ("never give up on a rejected fix") that differs from the default of
        // 168, and EF's usual sentinel (the CLR default, 0) would leave it out of the
        // INSERT so that the database default silently replaced it. See
        // DeviceConfigVersionConfiguration for the full account.
        builder.Property(profile => profile.MovingRetryMaxAgeHours)
            .HasColumnName("moving_retry_max_age_h")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingRetryMaxAgeHours)
            .HasSentinel(DeviceConfigRules.DefaultMovingRetryMaxAgeHours)
            .IsRequired();

        builder.Property(profile => profile.MovingConfigCheckSeconds)
            .HasColumnName("moving_config_check_s")
            .HasDefaultValue(DeviceConfigRules.DefaultMovingConfigCheckSeconds)
            .IsRequired();

        builder.Property(profile => profile.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(profile => profile.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(profile => profile.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        // Cascade: profiles describe a device, so a device genuinely removed from the
        // table leaves them nothing to describe. Note this is not the retirement path
        // — deleting a device is a soft delete, which leaves these rows untouched.
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(profile => profile.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, matching the revision table: an account being removed must not take
        // a device's schedule with it. The author becoming unknown is acceptable.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(profile => profile.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
