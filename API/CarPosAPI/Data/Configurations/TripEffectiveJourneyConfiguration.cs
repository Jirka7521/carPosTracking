using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// Maps <see cref="TripEffectiveJourney"/> onto the <c>trip_effective_journeys</c> view.
/// Keyless and view-mapped, so EF neither tracks its rows nor ever puts it in a migration;
/// the view's SQL lives in the <c>AddJourneyLocationRules</c> migration.
/// </summary>
public sealed class TripEffectiveJourneyConfiguration : IEntityTypeConfiguration<TripEffectiveJourney>
{
    /// <summary>Configures the view mapping.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<TripEffectiveJourney> builder)
    {
        builder.HasNoKey();
        builder.ToView("trip_effective_journeys");

        builder.Property(row => row.TripId).HasColumnName("trip_id");
        builder.Property(row => row.DeviceId).HasColumnName("device_id");
        builder.Property(row => row.StartTime).HasColumnName("start_time");
        builder.Property(row => row.EndTime).HasColumnName("end_time");
        builder.Property(row => row.NextStart).HasColumnName("next_start");
        builder.Property(row => row.JourneyPinned).HasColumnName("journey_pinned");
        builder.Property(row => row.Name).HasColumnName("name");
        builder.Property(row => row.EffectiveJourneyId).HasColumnName("effective_journey_id");
        builder.Property(row => row.JourneySource).HasColumnName("journey_source");
        builder.Property(row => row.LocationRuleId).HasColumnName("location_rule_id");
        builder.Property(row => row.LocationRuleAtEnd).HasColumnName("location_rule_at_end");
    }
}
