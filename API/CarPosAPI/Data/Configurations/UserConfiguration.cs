using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarPosAPI.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="User"/>. Explicit snake_case names for consistency
/// with <see cref="DeviceConfiguration"/> and <see cref="PositionConfiguration"/>,
/// whose raw ingest SQL depends on them.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>Maximum stored email length — comfortably above RFC 5321's 254.</summary>
    private const int EmailMaxLength = 256;

    /// <summary>
    /// Upper bound on the stored PBKDF2 string. ASP.NET Core's v3 format is 84
    /// base64 characters; 256 leaves room for a future format change without a
    /// migration.
    /// </summary>
    private const int PasswordHashMaxLength = 256;

    /// <summary>Maximum length of either name part.</summary>
    private const int NameMaxLength = 128;

    /// <summary>Configures the users table.</summary>
    /// <param name="builder">Type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .HasColumnName("id");

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(EmailMaxLength)
            .IsRequired();

        // The application lower-cases every email before it is stored or compared,
        // so a plain unique index is enough — no citext extension needed, and the
        // index stays usable by the equality lookup the login path performs.
        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("ux_users_email");

        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(PasswordHashMaxLength)
            .IsRequired();

        builder.Property(user => user.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(NameMaxLength)
            .IsRequired();

        builder.Property(user => user.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(NameMaxLength)
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
    }
}
