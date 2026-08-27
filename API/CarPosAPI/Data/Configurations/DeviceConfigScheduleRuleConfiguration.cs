using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="DeviceConfigScheduleRule"/>. House style throughout:
/// snake_case columns spelled out, CHECK constraints from <see cref="ScheduleRules"/>.
///
/// <para>
/// The one decision worth calling out is the profile foreign key, which
/// <b>restricts</b> rather than cascades. Deleting a profile that a rule still points
/// at would silently delete the rule with it — an hour of the week would stop being
/// covered and the only evidence would be the tracker quietly changing behaviour.
/// Restricting turns that into a 409 the dashboard can explain: "Night is used by 2
/// rules."
/// </para>
/// </summary>
public sealed class DeviceConfigScheduleRuleConfiguration : IEntityTypeConfiguration<DeviceConfigScheduleRule>
{
    /// <summary>Configures the device_config_schedule_rules table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DeviceConfigScheduleRule> builder)
    {
        builder.ToTable(
            "device_config_schedule_rules",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_device_config_schedule_rules_days_mask",
                    $"days_mask_utc BETWEEN {ScheduleRules.MinDaysMask} AND {ScheduleRules.MaxDaysMask}");
                table.HasCheckConstraint(
                    "ck_device_config_schedule_rules_start_minute",
                    $"start_minute_utc BETWEEN {ScheduleRules.MinStartMinute} AND {ScheduleRules.MaxStartMinute}");
                table.HasCheckConstraint(
                    "ck_device_config_schedule_rules_duration",
                    $"duration_minutes BETWEEN {ScheduleRules.MinDurationMinutes} AND {ScheduleRules.MaxDurationMinutes}");
                table.HasCheckConstraint(
                    "ck_device_config_schedule_rules_priority",
                    $"priority BETWEEN {ScheduleRules.MinPriority} AND {ScheduleRules.MaxPriority}");
            });

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(rule => rule.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        builder.Property(rule => rule.ProfileId)
            .HasColumnName("profile_id")
            .IsRequired();

        // The evaluator's access path: every rule for one device, cheapest first.
        builder.HasIndex(rule => rule.DeviceId)
            .HasDatabaseName("ix_device_config_schedule_rules_device_id");

        // Makes the "is this profile still in use?" check that guards a profile delete
        // an index lookup rather than a scan of the whole table.
        builder.HasIndex(rule => rule.ProfileId)
            .HasDatabaseName("ix_device_config_schedule_rules_profile_id");

        builder.Property(rule => rule.DaysMaskUtc)
            .HasColumnName("days_mask_utc")
            .IsRequired();

        builder.Property(rule => rule.StartMinuteUtc)
            .HasColumnName("start_minute_utc")
            .IsRequired();

        builder.Property(rule => rule.DurationMinutes)
            .HasColumnName("duration_minutes")
            .IsRequired();

        builder.Property(rule => rule.Priority)
            .HasColumnName("priority")
            .HasDefaultValue(ScheduleRules.DefaultPriority)
            .IsRequired();

        builder.Property(rule => rule.IsEnabled)
            .HasColumnName("is_enabled")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(rule => rule.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        // Cascade to the device, for the same reason as the profiles table.
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(rule => rule.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict to the profile — see the class summary.
        builder.HasOne<DeviceConfigProfile>()
            .WithMany()
            .HasForeignKey(rule => rule.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
