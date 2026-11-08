using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>EF mapping for <see cref="TripDetectionState"/>, one row per device.</summary>
public sealed class TripDetectionStateConfiguration : IEntityTypeConfiguration<TripDetectionState>
{
    /// <summary>Configures the trip_detection_state table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<TripDetectionState> builder)
    {
        builder.ToTable("trip_detection_state", table =>
        {
            table.HasCheckConstraint("ck_trip_detection_state_epoch", "epoch >= 0");
        });

        builder.HasKey(state => state.DeviceId);

        builder.Property(state => state.DeviceId)
            .HasColumnName("device_id");

        builder.HasOne<Device>()
            .WithOne()
            .HasForeignKey<TripDetectionState>(state => state.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(state => state.DirtyFrom)
            .HasColumnName("dirty_from");

        builder.Property(state => state.Epoch)
            .HasColumnName("epoch")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(state => state.LastPassAt)
            .HasColumnName("last_pass_at");
    }
}
