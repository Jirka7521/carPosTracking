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

        builder.Property(device => device.ConfigVersion)
            .HasColumnName("config_version")
            .HasDefaultValue(Dtos.DeviceConfigRules.InitialVersion);

        builder.Property(device => device.ConfigAppliedVersion)
            .HasColumnName("config_applied_version");

        builder.Property(device => device.ConfigAppliedAt)
            .HasColumnName("config_applied_at");

        builder.Property(device => device.ConfigScheduleEnabled)
            .HasColumnName("config_schedule_enabled")
            .HasDefaultValue(false);

        builder.Property(device => device.ConfigScheduleFallbackProfileId)
            .HasColumnName("config_schedule_fallback_profile_id");

        builder.Property(device => device.ConfigOverrideUntil)
            .HasColumnName("config_override_until");

        builder.Property(device => device.ConfigScheduleEvaluatedAt)
            .HasColumnName("config_schedule_evaluated_at");

        // SetNull, not Restrict as on the rules table. The asymmetry is deliberate: a
        // rule without its profile is broken and must be prevented, whereas a schedule
        // without a fallback is merely incomplete — the service refuses to *enable* one
        // in that state, and until then there is nothing to protect. Restricting here
        // would mean a profile could not be deleted while some disabled schedule from
        // months ago still named it.
        builder.HasOne<DeviceConfigProfile>()
            .WithMany()
            .HasForeignKey(device => device.ConfigScheduleFallbackProfileId)
            .OnDelete(DeleteBehavior.SetNull);

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
