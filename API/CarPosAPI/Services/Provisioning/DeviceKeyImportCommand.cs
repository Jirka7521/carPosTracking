using System.Security.Cryptography;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// The <c>import-device-key</c> CLI mode:
/// <c>dotnet run -- import-device-key --device GNSS01 --pem receiver_private.pem
/// [--public-pem receiver_public.pem] [--name "My car"]</c>.
/// Runs after the host is built but instead of starting it, so DI, configuration
/// and user-secrets behave exactly as in the web path while Kestrel and the MQTT
/// service never start. Reads the receiver's RSA-3072 private key PEM, encrypts
/// it under the master key and upserts the device row. This class writes to the
/// console deliberately — it is an interactive command, not service code — but
/// never prints key material, only a public-key fingerprint.
/// </summary>
internal static class DeviceKeyImportCommand
{
    /// <summary>The command word that selects this mode.</summary>
    private const string CommandName = "import-device-key";

    /// <summary>Process exit code for any provisioning failure.</summary>
    private const int ExitFailure = 1;

    /// <summary>The only key size the firmware ecosystem uses.</summary>
    private const int ExpectedRsaKeySizeBits = 3072;

    /// <summary>Checks whether the process was started in import mode.</summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns><c>true</c> when the first argument is the command word.</returns>
    public static bool IsRequested(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Executes the import.</summary>
    /// <param name="services">The built application's service provider.</param>
    /// <param name="args">Raw command-line arguments (parsed manually — the
    /// positional command word makes IConfiguration mapping unreliable).</param>
    /// <param name="cancellationToken">Cancellation (Ctrl+C).</param>
    /// <returns>Process exit code.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken cancellationToken = default)
    {
        string? deviceId = GetArgumentValue(args, "--device");
        string? pemPath = GetArgumentValue(args, "--pem");
        string? publicPemPath = GetArgumentValue(args, "--public-pem");
        string? displayName = GetArgumentValue(args, "--name");

        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(pemPath))
        {
            await Console.Error.WriteLineAsync(
                $"Usage: dotnet run -- {CommandName} --device <deviceId> --pem <private-key.pem> [--public-pem <public-key.pem>] [--name <display name>]");
            return ExitFailure;
        }

        if (!File.Exists(pemPath))
        {
            await Console.Error.WriteLineAsync($"Private key file not found: {pemPath}");
            return ExitFailure;
        }

        string privateKeyPem = await File.ReadAllTextAsync(pemPath, cancellationToken);

        using RSA privateKey = RSA.Create();
        try
        {
            privateKey.ImportFromPem(privateKeyPem);
        }
        catch (ArgumentException)
        {
            await Console.Error.WriteLineAsync("The file does not contain a valid PEM private key.");
            return ExitFailure;
        }

        if (privateKey.KeySize != ExpectedRsaKeySizeBits)
        {
            await Console.Error.WriteLineAsync(
                $"Key size is {privateKey.KeySize} bits; the firmware ecosystem uses RSA-{ExpectedRsaKeySizeBits}.");
            return ExitFailure;
        }

        string? publicKeyPem = null;
        if (!string.IsNullOrWhiteSpace(publicPemPath))
        {
            if (!File.Exists(publicPemPath))
            {
                await Console.Error.WriteLineAsync($"Public key file not found: {publicPemPath}");
                return ExitFailure;
            }

            publicKeyPem = await File.ReadAllTextAsync(publicPemPath, cancellationToken);
            if (!KeysMatch(privateKey, publicKeyPem))
            {
                await Console.Error.WriteLineAsync(
                    "The public key does not pair with the private key — refusing to store a mismatched pair.");
                return ExitFailure;
            }
        }

        // Scope so scoped services (DbContext) resolve correctly outside a request.
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IMasterKeyProtector protector = scope.ServiceProvider.GetRequiredService<IMasterKeyProtector>();
        IDbContextFactory<CarPosDbContext> contextFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<CarPosDbContext>>();

        byte[] protectedKey = protector.Protect(privateKeyPem, deviceId);

        await using CarPosDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Device? device = await context.Devices
            .SingleOrDefaultAsync(candidate => candidate.DeviceId == deviceId, cancellationToken);

        bool created = false;
        if (device is null)
        {
            device = new Device
            {
                DeviceId = deviceId,
                IsActive = true,
            };
            context.Devices.Add(device);
            created = true;
        }

        device.PrivateKeyCiphertext = protectedKey;
        if (publicKeyPem is not null)
        {
            device.PublicKeyPem = publicKeyPem;
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            device.DisplayName = displayName;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Fingerprint (SPKI SHA-256) identifies the key without revealing it.
        byte[] spki = privateKey.ExportSubjectPublicKeyInfo();
        string fingerprint = Convert.ToHexString(SHA256.HashData(spki));
        Console.WriteLine(created
            ? $"Created device '{deviceId}' with an encrypted private key (SPKI-SHA256 {fingerprint})."
            : $"Updated device '{deviceId}' with an encrypted private key (SPKI-SHA256 {fingerprint}).");
        Console.WriteLine("Restart the API if it is running — device keys are cached at load time.");
        return 0;
    }

    /// <summary>Verifies a public PEM pairs with the private key via a round trip.</summary>
    /// <param name="privateKey">The imported private key.</param>
    /// <param name="publicKeyPem">Candidate public key PEM.</param>
    /// <returns><c>true</c> when encrypt-with-public / decrypt-with-private round-trips.</returns>
    private static bool KeysMatch(RSA privateKey, string publicKeyPem)
    {
        try
        {
            using RSA publicKey = RSA.Create();
            publicKey.ImportFromPem(publicKeyPem);

            byte[] probe = RandomNumberGenerator.GetBytes(16);
            byte[] wrapped = publicKey.Encrypt(probe, RSAEncryptionPadding.OaepSHA256);
            byte[] unwrapped = privateKey.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256);
            return CryptographicOperations.FixedTimeEquals(probe, unwrapped);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Reads the value following a named argument.</summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="name">The argument name (e.g. <c>--device</c>).</param>
    /// <returns>The value, or null when absent.</returns>
    private static string? GetArgumentValue(string[] args, string name)
    {
        for (int index = 1; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
