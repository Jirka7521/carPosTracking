using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="Journey"/>. The CHECKs hold what the database can see on
/// one row (a range is whole and forward, a journey is not its own parent); the rules
/// that span rows — depth, cycles, overlapping ranges, the per-device cap — live in
/// <c>Services.Trips.JourneyService</c>, behind the device's trip lock.
/// </summary>
public sealed class JourneyConfiguration : IEntityTypeConfiguration<Journey>
{
    /// <summary>Configures the journeys table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Journey> builder)
    {
        builder.ToTable("journeys", table =>
        {
            table.HasCheckConstraint(
                "ck_journeys_range",
                "(range_from IS NULL AND range_to IS NULL) OR (range_from IS NOT NULL AND range_to IS NOT NULL AND range_from < range_to)");
            table.HasCheckConstraint("ck_journeys_not_own_parent", "parent_id IS NULL OR parent_id <> id");
            table.HasCheckConstraint("ck_journeys_name_not_blank", "length(btrim(name)) > 0");
        });

        builder.HasKey(journey => journey.Id);

        builder.Property(journey => journey.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(journey => journey.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        // Cascade, like schedule profiles: journeys describe a device's trips. The
        // erasure paths still delete them explicitly, so this only matters if a device
        // row is ever removed some other way.
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(journey => journey.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(journey => journey.ParentId)
            .HasColumnName("parent_id");

        // Restrict: deleting a journey moves its children up a level first, by hand.
        builder.HasOne<Journey>()
            .WithMany()
            .HasForeignKey(journey => journey.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(journey => journey.Name)
            .HasColumnName("name")
            .HasMaxLength(TripRules.MaxNameLength)
            .IsRequired();

        builder.Property(journey => journey.RangeFrom)
            .HasColumnName("range_from");

        builder.Property(journey => journey.RangeTo)
            .HasColumnName("range_to");

        builder.Property(journey => journey.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(journey => journey.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        // Names are unique among siblings. NULLS NOT DISTINCT so two top-level journeys
        // (parent NULL) cannot share a name either — with the default, NULL parents
        // would never collide. Also serves "children of X" lookups.
        builder.HasIndex(journey => new { journey.DeviceId, journey.ParentId, journey.Name })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_journeys_device_id_parent_id_name");

        // The Restrict check on parent_id when a journey is deleted.
        builder.HasIndex(journey => journey.ParentId)
            .HasFilter("parent_id IS NOT NULL")
            .HasDatabaseName("ix_journeys_parent_id");

        // Finding the journey whose range contains a trip start.
        builder.HasIndex(journey => new { journey.DeviceId, journey.RangeFrom })
            .HasFilter("range_from IS NOT NULL")
            .HasDatabaseName("ix_journeys_device_id_range_from");
    }
}
