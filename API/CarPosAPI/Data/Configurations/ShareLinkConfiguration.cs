using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="ShareLink"/>. Explicit snake_case names, matching the
/// sibling configurations.
/// </summary>
public sealed class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
{
    /// <summary>
    /// Base64Url of 16 bytes is 22 characters; the column is sized to exactly that
    /// so a value of any other length cannot be stored at all. The lengths here
    /// and the byte counts in <see cref="Services.Sharing.ShareTokenFactory"/> are
    /// two statements of one fact — change either and the other must follow.
    /// </summary>
    private const int SelectorLength = 22;

    /// <summary>Base64 of a SHA-256 digest is 44 characters including its padding.</summary>
    private const int VerifierHashLength = 44;

    /// <summary>
    /// Identity's PBKDF2 format is 84 characters today. The column is generously
    /// sized rather than exact because the framework owns that format and has
    /// changed it before — the same reasoning, and the same 256, as
    /// <c>users.password_hash</c>.
    /// </summary>
    private const int PassphraseHashMaxLength = 256;

    /// <summary>Configures the share_links table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<ShareLink> builder)
    {
        builder.ToTable("share_links");

        builder.HasKey(link => link.Id);

        builder.Property(link => link.Id)
            .HasColumnName("id");

        builder.Property(link => link.Selector)
            .HasColumnName("selector")
            .HasMaxLength(SelectorLength)
            .IsRequired();

        builder.Property(link => link.VerifierHash)
            .HasColumnName("verifier_hash")
            .HasMaxLength(VerifierHashLength)
            .IsRequired();

        builder.Property(link => link.PassphraseHash)
            .HasColumnName("passphrase_hash")
            .HasMaxLength(PassphraseHashMaxLength)
            .IsRequired();

        builder.Property(link => link.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        // Nullable for the same reason as accesses.granted_by — see the entity.
        builder.Property(link => link.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        builder.Property(link => link.Label)
            .HasColumnName("label")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(link => link.ValidFrom)
            .HasColumnName("valid_from")
            .IsRequired();

        builder.Property(link => link.ValidUntil)
            .HasColumnName("valid_until")
            .IsRequired();

        // Int-valued like ConfigRevisionSource; the enum pins its ordinals.
        builder.Property(link => link.Scope)
            .HasColumnName("scope")
            .HasConversion<int>()
            .HasDefaultValue(ShareScope.LatestOnly)
            .IsRequired();

        // Both telemetry flags default to off. A share that was created without
        // thinking about them discloses coordinates and nothing else.
        builder.Property(link => link.IncludeSpeed)
            .HasColumnName("include_speed")
            .HasDefaultValue(false);

        builder.Property(link => link.IncludeTelemetry)
            .HasColumnName("include_telemetry")
            .HasDefaultValue(false);

        builder.Property(link => link.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(link => link.FailedAttempts)
            .HasColumnName("failed_attempts")
            .HasDefaultValue(0);

        builder.Property(link => link.LockedUntil)
            .HasColumnName("locked_until");

        builder.Property(link => link.SuccessfulRedeems)
            .HasColumnName("successful_redeems")
            .HasDefaultValue(0);

        builder.Property(link => link.LastAccessedAt)
            .HasColumnName("last_accessed_at");

        builder.Property(link => link.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        // Restrict, like accesses: a share link is a record of a disclosure, so a
        // device may not be deleted out from under one. (Devices are soft-deleted
        // anyway; the one path that truly removes them is Art. 17 erasure, which
        // clears the links itself and in the right order.)
        builder.HasOne(link => link.Device)
            .WithMany()
            .HasForeignKey(link => link.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        // The redeem path's only lookup, and the reason the token is split in two:
        // this index is what makes finding a link an indexed equality probe rather
        // than a scan hashing every row. Unique because the selector *is* the
        // identity of a link — a collision would make SingleOrDefault throw, which
        // is a far better failure than resolving the wrong share.
        builder.HasIndex(link => link.Selector)
            .IsUnique()
            .HasDatabaseName("ux_share_links_selector");

        // "Every link on this device" — the creator's management list, which shows
        // revoked and expired links too so that "who did I give this to, and when
        // did I withdraw it" stays answerable. Unfiltered for exactly that reason,
        // matching ix_accesses_device_id.
        builder.HasIndex(link => link.DeviceId)
            .HasDatabaseName("ix_share_links_device_id");
    }
}
