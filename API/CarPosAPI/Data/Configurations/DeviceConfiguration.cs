using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="Device"/>. Table and column names are spelled out in
/// snake_case explicitly (no naming-convention plugin) because the ingest write
/// path uses raw SQL that hard-codes these names — an implicit rename would break
/// it silently.
/// </summary>
public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    /// <summary>Configures the devices table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(device => device.Id);

        builder.Property(device => device.Id)
            .HasColumnName("id")
            // gen_random_uuid() is built into PostgreSQL 13+; no extension needed.
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(device => device.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(64)
            .IsRequired();

        // The MQTT identity must be unique — it is how incoming topics resolve to a row.
        builder.HasIndex(device => device.DeviceId)
            .IsUnique()
            .HasDatabaseName("ux_devices_device_id");

        builder.Property(device => device.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(128);

        builder.Property(device => device.PublicKeyPem)
            .HasColumnName("public_key_pem");

        builder.Property(device => device.PrivateKeyCiphertext)
            .HasColumnName("private_key_ciphertext");

        builder.Property(device => device.AckPublicKeyPem)
            .HasColumnName("ack_public_key_pem");

        builder.Property(device => device.LastSeenAt)
            .HasColumnName("last_seen_at");

        builder.Property(device => device.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(device => device.DeactivatedAt)
            .HasColumnName("deactivated_at");

        builder.Property(device => device.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
    }
}
