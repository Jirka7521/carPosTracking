using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="RegistrationInvite"/>. Explicit snake_case names,
/// matching the sibling configurations.
/// </summary>
public sealed class RegistrationInviteConfiguration : IEntityTypeConfiguration<RegistrationInvite>
{
    /// <summary>A SHA-256 digest in lower-case hex is exactly 64 characters.</summary>
    private const int CodeHashLength = 64;

    /// <summary>Configures the registration_invites table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<RegistrationInvite> builder)
    {
        builder.ToTable("registration_invites");

        builder.HasKey(invite => invite.Id);

        builder.Property(invite => invite.Id)
            .HasColumnName("id");

        builder.Property(invite => invite.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(CodeHashLength)
            .IsFixedLength()
            .IsRequired();

        builder.Property(invite => invite.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(invite => invite.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(invite => invite.UsedAt)
            .HasColumnName("used_at")
            .HasColumnType("timestamp with time zone");

        // Registration looks a code up by its digest, and two codes must never share
        // one — the unique index is also what makes that lookup a single row.
        builder.HasIndex(invite => invite.CodeHash)
            .IsUnique()
            .HasDatabaseName("ux_registration_invites_code_hash");
    }
}
