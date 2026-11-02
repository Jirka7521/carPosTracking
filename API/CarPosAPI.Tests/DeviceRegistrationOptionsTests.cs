using System.ComponentModel.DataAnnotations;
using CarPosAPI.Data;
using CarPosAPI.Options;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers the per-account device ceiling: that its default is a real limit, that
/// the configuration cannot switch it off, and that the count behind it runs in the
/// database (a count that came home as rows would be the very load the limit is
/// there to prevent).
/// </summary>
public sealed class DeviceRegistrationOptionsTests
{
    /// <summary>Never connected to — the provider only needs it to be well-formed.</summary>
    private const string UnusedConnectionString =
        "Host=localhost;Database=carpos_translation_check;Username=none;Password=none";

    [Fact]
    public void TheDefaultIsABoundedNumber()
    {
        DeviceRegistrationOptions options = new DeviceRegistrationOptions();

        Assert.Equal(20, options.MaxActiveDevicesPerUser);
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000)]
    public void AValueOutsideTheRangeFailsValidation(int limit)
    {
        Assert.NotEmpty(Validate(new DeviceRegistrationOptions { MaxActiveDevicesPerUser = limit }));
    }

    [Fact]
    public void TheRegisteredDeviceFilterRunsInSql()
    {
        DbContextOptions<CarPosDbContext> options = new DbContextOptionsBuilder<CarPosDbContext>()
            .UseNpgsql(UnusedConnectionString)
            .Options;

        using CarPosDbContext context = new CarPosDbContext(options);

        int userId = 42;

        // The same predicate as DeviceService.CreateAsync.
        string sql = context.Accesses
            .AsNoTracking()
            .Where(access => access.UserId == userId
                && access.IsOwner
                && access.IsActive
                && access.Device!.IsActive)
            .Select(access => access.Id)
            .ToQueryString();

        Assert.Contains("is_owner", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("devices", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs the DataAnnotations on an options instance, as ValidateDataAnnotations does.</summary>
    /// <param name="options">The instance to check.</param>
    /// <returns>Every validation failure.</returns>
    private static List<ValidationResult> Validate(DeviceRegistrationOptions options)
    {
        List<ValidationResult> results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
