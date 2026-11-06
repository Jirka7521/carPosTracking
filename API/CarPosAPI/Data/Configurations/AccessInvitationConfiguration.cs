using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="AccessInvitation"/>. Explicit snake_case names,
/// matching the sibling configurations; the token columns are sized exactly like
/// <see cref="ShareLinkConfiguration"/>'s, because they are the same scheme.
/// </summary>
public sealed class AccessInvitationConfiguration : IEntityTypeConfiguration<AccessInvitation>
{
    /// <summary>16 random bytes in unpadded base64url.</summary>
    private const int SelectorLength = 22;

    /// <summary>A SHA-256 digest in lower-case hex.</summary>
    private const int SecretHashLength = 64;

    /// <summary>Configures the access_invitations table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AccessInvitation> builder)
    {
        builder.ToTable("access_invitations");

        builder.HasKey(invitation => invitation.Id);

        builder.Property(invitation => invitation.Id)
            .HasColumnName("id");

        builder.Property(invitation => invitation.Selector)
            .HasColumnName("selector")
            .HasMaxLength(SelectorLength)
            .IsRequired();

        builder.Property(invitation => invitation.VerifierHash)
            .HasColumnName("verifier_hash")
            .HasMaxLength(SecretHashLength)
            .IsRequired();

        builder.Property(invitation => invitation.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        builder.Property(invitation => invitation.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(invitation => invitation.CanDelete)
            .HasColumnName("can_delete")
            .IsRequired();

        builder.Property(invitation => invitation.CanShare)
            .HasColumnName("can_share")
            .IsRequired();

        builder.Property(invitation => invitation.CanModifySettings)
            .HasColumnName("can_modify_settings")
            .IsRequired();

        builder.Property(invitation => invitation.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(invitation => invitation.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Restrict, like share links: devices are only soft-deleted, and the one path
        // that physically removes a device (Art. 17 erasure) deletes its invitations
        // itself, first.
        builder.HasOne(invitation => invitation.Device)
            .WithMany()
            .HasForeignKey(invitation => invitation.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Opening, accepting and declining all find the row by its selector.
        builder.HasIndex(invitation => invitation.Selector)
            .IsUnique()
            .HasDatabaseName("ux_access_invitations_selector");

        // The sharing list and the pending-invitation cap both filter by device.
        builder.HasIndex(invitation => invitation.DeviceId)
            .HasDatabaseName("ix_access_invitations_device_id");
    }
}
