using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="DeviceTripSettings"/>. Every threshold is CHECK-bounded
/// from <see cref="TripRules"/>, the same constants behind the request DTO's
/// <c>[Range]</c> attributes, so the worker can trust whatever it reads.
/// </summary>
public sealed class DeviceTripSettingsConfiguration : IEntityTypeConfiguration<DeviceTripSettings>
{
    /// <summary>Configures the device_trip_settings table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DeviceTripSettings> builder)
    {
        builder.ToTable("device_trip_settings", table =>
        {
            table.HasCheckConstraint(
                "ck_device_trip_settings_min_stop_seconds",
                $"min_stop_seconds BETWEEN {TripRules.MinStopSecondsLowerBound} AND {TripRules.MinStopSecondsUpperBound}");
            table.HasCheckConstraint(
                "ck_device_trip_settings_stop_radius_m",
                $"stop_radius_m BETWEEN {TripRules.MinStopRadiusMeters} AND {TripRules.MaxStopRadiusMeters}");
            table.HasCheckConstraint(
                "ck_device_trip_settings_min_trip_distance_m",
                $"min_trip_distance_m BETWEEN {TripRules.MinTripDistanceLowerBound} AND {TripRules.MinTripDistanceUpperBound}");
        });

        builder.HasKey(settings => settings.DeviceId);

        builder.Property(settings => settings.DeviceId)
            .HasColumnName("device_id");

        builder.HasOne<Device>()
            .WithOne()
            .HasForeignKey<DeviceTripSettings>(settings => settings.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(settings => settings.MinStopSeconds)
            .HasColumnName("min_stop_seconds")
            .IsRequired();

        builder.Property(settings => settings.StopRadiusMeters)
            .HasColumnName("stop_radius_m")
            .IsRequired();

        builder.Property(settings => settings.MinTripDistanceMeters)
            .HasColumnName("min_trip_distance_m")
            .IsRequired();

        builder.Property(settings => settings.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");
    }
}
