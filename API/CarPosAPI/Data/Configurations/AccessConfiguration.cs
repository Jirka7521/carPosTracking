using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="Access"/>. Explicit snake_case names, matching the
/// sibling configurations.
/// </summary>
public sealed class AccessConfiguration : IEntityTypeConfiguration<Access>
{
    /// <summary>Configures the accesses table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Access> builder)
    {
        builder.ToTable("accesses");

        builder.HasKey(access => access.Id);

        builder.Property(access => access.Id)
            .HasColumnName("id");

        builder.Property(access => access.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(access => access.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        builder.Property(access => access.GrantedBy)
            .HasColumnName("granted_by")
            .IsRequired();

        builder.Property(access => access.CanRead)
            .HasColumnName("can_read")
            .HasDefaultValue(true);

        builder.Property(access => access.CanDelete)
            .HasColumnName("can_delete")
            .HasDefaultValue(false);

        builder.Property(access => access.CanShare)
            .HasColumnName("can_share")
            .HasDefaultValue(false);

        builder.Property(access => access.CanModifySettings)
            .HasColumnName("can_modify_settings")
            .HasDefaultValue(false);

        builder.Property(access => access.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(access => access.DateRegistration)
            .HasColumnName("date_registration")
            .HasDefaultValueSql("now()");

        // Grants are history: revoking deactivates the row, so neither a user nor a
        // device may ever be deleted out from under one.
        builder.HasOne(access => access.User)
            .WithMany()
            .HasForeignKey(access => access.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(access => access.Device)
            .WithMany()
            .HasForeignKey(access => access.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one *active* grant per (user, device). The filter is what makes
        // re-granting after a revoke possible: the revoked row stays for the audit
        // trail and drops out of the index, so a fresh grant does not collide with
        // it. This uniqueness is also what lets the authorizer trust a
        // SingleOrDefault lookup.
        builder.HasIndex(access => new { access.UserId, access.DeviceId })
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("ux_accesses_user_id_device_id_active");

        // The hot path is "everything this user can see" (GET /api/me/devices) and
        // "everyone who can see this device" (GET /api/access?deviceId=).
        builder.HasIndex(access => access.DeviceId)
            .HasDatabaseName("ix_accesses_device_id");
    }
}
