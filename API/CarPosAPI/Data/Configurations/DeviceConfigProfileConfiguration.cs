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
