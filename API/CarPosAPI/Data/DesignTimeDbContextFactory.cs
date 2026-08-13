using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CarPosAPI.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without booting the real
/// application (which insists on real secrets and would fail fast without them).
/// The connection string here is a placeholder that is never opened: adding a
/// migration only needs the compiled model, and <c>dotnet ef database update</c>
/// is always run with an explicit <c>--connection</c> (admin role) per README.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CarPosDbContext>
{
    /// <summary>Creates a context for design-time tooling only.</summary>
    /// <param name="args">Arguments passed by the EF tools (unused).</param>
    /// <returns>A context configured for Npgsql with a dummy connection string.</returns>
    public CarPosDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CarPosDbContext> optionsBuilder = new DbContextOptionsBuilder<CarPosDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=carpos;Username=design_time_only");
        return new CarPosDbContext(optionsBuilder.Options);
    }
}
