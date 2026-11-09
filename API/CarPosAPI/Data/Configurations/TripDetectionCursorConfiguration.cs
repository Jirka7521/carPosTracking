using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="TripDetectionCursor"/>. The table holds exactly one row,
/// seeded by the migration at position id 0 — which is what makes the worker's first
/// pass a backfill of the whole history.
/// </summary>
public sealed class TripDetectionCursorConfiguration : IEntityTypeConfiguration<TripDetectionCursor>
{
    /// <summary>Configures the trip_detection_cursor table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<TripDetectionCursor> builder)
    {
        builder.ToTable("trip_detection_cursor", table =>
        {
            table.HasCheckConstraint("ck_trip_detection_cursor_singleton", $"id = {TripDetectionCursor.SingletonId}");
            table.HasCheckConstraint("ck_trip_detection_cursor_last_position_id", "last_position_id >= 0");
        });

        builder.HasKey(cursor => cursor.Id);

        builder.Property(cursor => cursor.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(cursor => cursor.LastPositionId)
            .HasColumnName("last_position_id")
            .IsRequired();

        builder.HasData(new TripDetectionCursor
        {
            Id = TripDetectionCursor.SingletonId,
            LastPositionId = 0,
        });
    }
}
