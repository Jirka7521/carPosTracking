using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="DeviceAlias"/>. Explicit snake_case names, matching
/// the sibling configurations.
/// </summary>
public sealed class DeviceAliasConfiguration : IEntityTypeConfiguration<DeviceAlias>
{
    /// <summary>Matches the display_name ceiling on the devices table.</summary>
    private const int AliasMaxLength = 128;

    /// <summary>Configures the device_aliases table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DeviceAlias> builder)
    {
        builder.ToTable("device_aliases");

        builder.HasKey(alias => alias.Id);

        builder.Property(alias => alias.Id)
            .HasColumnName("id");

        builder.Property(alias => alias.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(alias => alias.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        builder.Property(alias => alias.Alias)
            .HasColumnName("alias")
            .HasMaxLength(AliasMaxLength)
            .IsRequired();

        builder.Property(alias => alias.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        // An alias is worthless without its user or device, and both of those are
        // soft-deleted rather than removed, so Restrict keeps the FKs honest.
        builder.HasOne(alias => alias.User)
            .WithMany()
            .HasForeignKey(alias => alias.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(alias => alias.Device)
            .WithMany()
            .HasForeignKey(alias => alias.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        // One alias per user per device — the upsert in the alias endpoint relies
        // on this to decide between insert and update.
        builder.HasIndex(alias => new { alias.UserId, alias.DeviceId })
            .IsUnique()
            .HasDatabaseName("ux_device_aliases_user_id_device_id");
    }
}
