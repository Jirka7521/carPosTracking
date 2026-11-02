using Microsoft.Extensions.Configuration;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins the contract between the deployment's mounted secret files and the API's
/// configuration keys. The compose file names each file after the key it fills
/// (<c>/run/secrets/Mqtt__Password</c>), Program.cs adds the key-per-file provider
/// after the environment, and if either side drifted the API would start with an
/// empty secret — refused by validation at best, a confusing broker 401 at worst.
/// </summary>
public sealed class ContainerSecretsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("carpos-secrets-").FullName;

    [Fact]
    public void AFileNamedByTheKeyLandsOnThatKey()
    {
        // No trailing newline, as the publisher writes them; the one an editor adds
        // is trimmed by the provider.
        File.WriteAllText(Path.Combine(_directory, "Mqtt__Password"), "from-file");
        File.WriteAllText(Path.Combine(_directory, "ConnectionStrings__CarPos"), "Host=postgres;Password=x\n");

        IConfiguration configuration = Build(environment: []);

        Assert.Equal("from-file", configuration["Mqtt:Password"]);
        Assert.Equal("Host=postgres;Password=x", configuration.GetConnectionString("CarPos"));
    }

    [Fact]
    public void AFileWinsOverAnEnvironmentVariable()
    {
        File.WriteAllText(Path.Combine(_directory, "Jwt__SigningKey"), "from-file");

        IConfiguration configuration = Build(environment: new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "from-environment",
        });

        Assert.Equal("from-file", configuration["Jwt:SigningKey"]);
    }

    [Fact]
    public void WithoutTheDirectoryTheEnvironmentStillWorks()
    {
        // Backward compatibility: a deployment that still passes variables, and
        // development, where /run/secrets does not exist.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = "from-environment" })
            .AddKeyPerFile(Path.Combine(_directory, "missing"), optional: true, reloadOnChange: false)
            .Build();

        Assert.Equal("from-environment", configuration["Jwt:SigningKey"]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Builds configuration in the order Program.cs does: environment, then files.</summary>
    /// <param name="environment">Stand-in for the environment variables.</param>
    /// <returns>The built configuration.</returns>
    private IConfiguration Build(Dictionary<string, string?> environment)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(environment)
            .AddKeyPerFile(_directory, optional: true, reloadOnChange: false)
            .Build();
    }
}
