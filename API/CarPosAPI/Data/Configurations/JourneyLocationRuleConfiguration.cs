using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="JourneyLocationRule"/>. The CHECKs hold what one row can
/// promise (a real point, a bounded radius, something to match, an end with its reason,
/// an active rule with a target); the per-device caps live in
/// <c>Services.Trips.LocationRuleService</c>, behind the device's trip lock.
///
/// <para>
/// The <c>center geography(Point,4326)</c> generated column is added by raw SQL in the
/// <c>AddJourneyLocationRules</c> migration and is deliberately not mapped here — the
/// same arrangement as <c>positions.location</c> (see <see cref="Position"/>).
/// </para>
/// </summary>
public sealed class JourneyLocationRuleConfiguration : IEntityTypeConfiguration<JourneyLocationRule>
{
    /// <summary>Configures the journey_location_rules table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<JourneyLocationRule> builder)
    {
        builder.ToTable("journey_location_rules", table =>
        {
            table.HasCheckConstraint("ck_journey_location_rules_latitude", "latitude >= -90 AND latitude <= 90");
            table.HasCheckConstraint("ck_journey_location_rules_longitude", "longitude >= -180 AND longitude <= 180");
            table.HasCheckConstraint(
                "ck_journey_location_rules_radius_m",
                $"radius_m BETWEEN {TripRules.MinLocationRuleRadiusMeters} AND {TripRules.MaxLocationRuleRadiusMeters}");
            table.HasCheckConstraint("ck_journey_location_rules_match", "match_start OR match_end");
            table.HasCheckConstraint("ck_journey_location_rules_name_not_blank", "name IS NULL OR length(btrim(name)) > 0");

            // An end and its reason come and go together, and the reason is one we know.
            table.HasCheckConstraint(
                "ck_journey_location_rules_end",
                $"(ended_at IS NULL AND end_reason IS NULL) OR (ended_at IS NOT NULL AND end_reason IN ('{LocationRuleEndReasons.User}', '{LocationRuleEndReasons.JourneyDeleted}'))");

            // A rule in force always has somewhere to put trips.
            table.HasCheckConstraint("ck_journey_location_rules_target", "journey_id IS NOT NULL OR ended_at IS NOT NULL");
        });

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(rule => rule.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        // Cascade, like journeys: the erasure paths delete rules explicitly, so this only
        // matters if a device row is ever removed some other way.
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(rule => rule.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(rule => rule.JourneyId)
            .HasColumnName("journey_id");

        // Restrict: deleting a journey retargets its rules to the parent (or ends them at
        // the top level) by hand first, so the history keeps saying where trips went.
        builder.HasOne<Journey>()
            .WithMany()
            .HasForeignKey(rule => rule.JourneyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(rule => rule.Name)
            .HasColumnName("name")
            .HasMaxLength(TripRules.MaxNameLength);

        builder.Property(rule => rule.Latitude)
            .HasColumnName("latitude")
            .IsRequired();

        builder.Property(rule => rule.Longitude)
            .HasColumnName("longitude")
            .IsRequired();

        builder.Property(rule => rule.RadiusMeters)
            .HasColumnName("radius_m")
            .IsRequired();

        builder.Property(rule => rule.MatchStart)
            .HasColumnName("match_start")
            .IsRequired();

        builder.Property(rule => rule.MatchEnd)
            .HasColumnName("match_end")
            .IsRequired();

        builder.Property(rule => rule.ValidFrom)
            .HasColumnName("valid_from")
            .IsRequired();

        builder.Property(rule => rule.EndedAt)
            .HasColumnName("ended_at");

        builder.Property(rule => rule.EndReason)
            .HasColumnName("end_reason")
            .HasMaxLength(20);

        builder.Property(rule => rule.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(rule => rule.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        // The view's access path: a device's rules in force at a trip's start.
        builder.HasIndex(rule => new { rule.DeviceId, rule.ValidFrom })
            .HasDatabaseName("ix_journey_location_rules_device_id_valid_from");

        // The Restrict check, and retargeting, when a journey is deleted.
        builder.HasIndex(rule => rule.JourneyId)
            .HasFilter("journey_id IS NOT NULL")
            .HasDatabaseName("ix_journey_location_rules_journey_id");
    }
}
