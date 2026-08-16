using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Data;

/// <summary>
/// The application's EF Core context — owner of the carpos schema via migrations.
/// Runtime connections use the least-privilege <c>BE</c> role (DML only);
/// migrations are applied manually as <c>admin</c> (see README). Registered
/// through <c>AddDbContextFactory</c> so singleton ingest services create
/// short-lived contexts safely while future controllers can still inject the
/// scoped context the factory registration also provides.
/// </summary>
public sealed class CarPosDbContext : DbContext
{
    /// <summary>Creates the context with externally configured options.</summary>
    /// <param name="options">Options (provider, connection string) from DI.</param>
    public CarPosDbContext(DbContextOptions<CarPosDbContext> options)
        : base(options)
    {
    }

    /// <summary>Registered tracker devices (including their protected key material).</summary>
    public DbSet<Device> Devices => Set<Device>();

    /// <summary>Decrypted, validated GNSS fixes.</summary>
    public DbSet<Position> Positions => Set<Position>();

    /// <summary>Dashboard accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Per-(user, device) capability grants — the entire authorisation model.</summary>
    public DbSet<Access> Accesses => Set<Access>();

    /// <summary>Per-user private nicknames for devices.</summary>
    public DbSet<DeviceAlias> DeviceAliases => Set<DeviceAlias>();

    /// <summary>Applies every IEntityTypeConfiguration in this assembly.</summary>
    /// <param name="modelBuilder">Model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CarPosDbContext).Assembly);
    }
}
