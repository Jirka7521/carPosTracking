using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="RevokedSession"/>. Explicit snake_case names,
/// matching the sibling configurations.
/// </summary>
public sealed class RevokedSessionConfiguration : IEntityTypeConfiguration<RevokedSession>
{
    /// <summary>A jti is a GUID in "N" form (32 characters); room for any other issuer's.</summary>
    private const int TokenIdMaxLength = 64;

    /// <summary>Configures the revoked_sessions table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<RevokedSession> builder)
    {
        builder.ToTable("revoked_sessions");

        builder.HasKey(session => session.TokenId);

        builder.Property(session => session.TokenId)
            .HasColumnName("token_id")
            .HasMaxLength(TokenIdMaxLength);

        builder.Property(session => session.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // The prune deletes by expiry on every sign-out.
        builder.HasIndex(session => session.ExpiresAt)
            .HasDatabaseName("ix_revoked_sessions_expires_at");
    }
}
