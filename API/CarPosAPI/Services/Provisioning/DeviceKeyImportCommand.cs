using System.Security.Cryptography;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// The <c>import-device-key</c> CLI mode:
/// <c>dotnet run -- import-device-key --device GNSS01 [--pem receiver_private.pem]
/// [--public-pem receiver_public.pem] [--ack-public-pem ack_public.pem]
/// [--name "My car"]</c>.
/// Runs after the host is built but instead of starting it, so DI, configuration
/// and user-secrets behave exactly as in the web path while Kestrel and the MQTT
/// service never start. Reads the receiver's RSA-3072 private key PEM, encrypts
/// it under the master key and upserts the device row. This class writes to the
/// console deliberately — it is an interactive command, not service code — but
/// never prints key material, only a public-key fingerprint.
///
/// <para>
/// <b>The two key directions are not symmetric.</b> <c>--pem</c> imports the
/// <em>receiver</em> private key, which this server needs to decrypt telemetry.
/// <c>--ack-public-pem</c> imports the <em>device's</em> public key, which the
/// server needs to seal delivery acks — its private half is generated off-server
/// and pasted straight into the firmware's git-ignored <c>Config.h</c>, so it never
/// reaches this database, any DTO, or the dashboard. Either may be given alone:
/// adding acks to an existing device must not require handling its private key.
/// </para>
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
        string? ackPublicPemPath = GetArgumentValue(args, "--ack-public-pem");
        string? displayName = GetArgumentValue(args, "--name");

        // --ack-public-pem alone is a valid invocation: adding delivery acks to a
        // device that was provisioned through the API must not require re-importing
        // (or even possessing) its receiver private key.
        bool hasAnyKeyArgument = !string.IsNullOrWhiteSpace(pemPath)
            || !string.IsNullOrWhiteSpace(ackPublicPemPath);

        if (string.IsNullOrWhiteSpace(deviceId) || !hasAnyKeyArgument)
        {
            await Console.Error.WriteLineAsync(
                $"Usage: dotnet run -- {CommandName} --device <deviceId> [--pem <private-key.pem>] "
                + "[--public-pem <public-key.pem>] [--ack-public-pem <ack-public-key.pem>] [--name <display name>]");
            await Console.Error.WriteLineAsync(
                "At least one of --pem (receiver private key) or --ack-public-pem (device ack public key) is required.");
            return ExitFailure;
        }

        // The receiver private key is optional now, so everything about it is loaded
        // into locals that stay null when --pem was not given.
        string? privateKeyPem = null;
        string? publicKeyPem = null;
        string? privateKeyFingerprint = null;
        RSA? privateKey = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(pemPath))
            {
                if (!File.Exists(pemPath))
                {
                    await Console.Error.WriteLineAsync($"Private key file not found: {pemPath}");
                    return ExitFailure;
                }

                privateKeyPem = await File.ReadAllTextAsync(pemPath, cancellationToken);

                privateKey = RSA.Create();
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

                // Fingerprint (SPKI SHA-256) identifies the key without revealing it.
                privateKeyFingerprint = Convert.ToHexString(
                    SHA256.HashData(privateKey.ExportSubjectPublicKeyInfo()));
            }
            else if (!string.IsNullOrWhiteSpace(publicPemPath))
            {
                await Console.Error.WriteLineAsync(
                    "--public-pem is only meaningful together with --pem; it is verified against that private key.");
                return ExitFailure;
            }

            // The device's ack public key. Note there is deliberately no pairing check
            // here as there is for --public-pem: the matching private key belongs to
            // the device alone and must never exist on this server, so we have nothing
            // to round-trip against and would not want it if we did.
            string? ackPublicKeyPem = null;
            string? ackFingerprint = null;
            if (!string.IsNullOrWhiteSpace(ackPublicPemPath))
            {
                if (!File.Exists(ackPublicPemPath))
                {
                    await Console.Error.WriteLineAsync($"Ack public key file not found: {ackPublicPemPath}");
                    return ExitFailure;
                }

                ackPublicKeyPem = await File.ReadAllTextAsync(ackPublicPemPath, cancellationToken);

                using RSA ackPublicKey = RSA.Create();
                try
                {
                    ackPublicKey.ImportFromPem(ackPublicKeyPem);
                }
                catch (ArgumentException)
                {
                    await Console.Error.WriteLineAsync("The ack key file does not contain a valid PEM public key.");
                    return ExitFailure;
                }

                if (ackPublicKey.KeySize != ExpectedRsaKeySizeBits)
                {
                    await Console.Error.WriteLineAsync(
                        $"Ack key size is {ackPublicKey.KeySize} bits; the firmware ecosystem uses RSA-{ExpectedRsaKeySizeBits}.");
                    return ExitFailure;
                }

                // Guard against the operator handing over the wrong half. A private PEM
                // would import fine and work, but storing it here would put a device
                // secret in the database and in every provisioning response.
                if (ackPublicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
                {
                    await Console.Error.WriteLineAsync(
                        "That file contains a PRIVATE key. Pass the ack PUBLIC key — the private half belongs only in the firmware's Config.h.");
                    return ExitFailure;
                }

                ackFingerprint = Convert.ToHexString(
                    SHA256.HashData(ackPublicKey.ExportSubjectPublicKeyInfo()));
            }

            // Scope so scoped services (DbContext) resolve correctly outside a request.
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            IMasterKeyProtector protector = scope.ServiceProvider.GetRequiredService<IMasterKeyProtector>();
            IDbContextFactory<CarPosDbContext> contextFactory =
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<CarPosDbContext>>();

            await using CarPosDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
            Device? device = await context.Devices
                .SingleOrDefaultAsync(candidate => candidate.DeviceId == deviceId, cancellationToken);

            bool created = false;
            if (device is null)
            {
                if (privateKeyPem is null)
                {
                    // An ack key alone cannot bootstrap a device: without a receiver
                    // private key the row could never decrypt telemetry, so creating it
                    // would only produce a device that silently drops everything.
                    await Console.Error.WriteLineAsync(
                        $"Device '{deviceId}' does not exist. Provision it first (or pass --pem) before importing an ack key.");
                    return ExitFailure;
                }

                device = new Device
                {
                    DeviceId = deviceId,
                    IsActive = true,
                };
                context.Devices.Add(device);
                created = true;
            }

            if (privateKeyPem is not null)
            {
                // Bound to this device id as GCM associated data, so the blob cannot be
                // moved onto another device's row and still decrypt.
                device.PrivateKeyCiphertext = protector.Protect(privateKeyPem, deviceId);
            }

            if (publicKeyPem is not null)
            {
                device.PublicKeyPem = publicKeyPem;
            }

            if (ackPublicKeyPem is not null)
            {
                device.AckPublicKeyPem = ackPublicKeyPem;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                device.DisplayName = displayName;
            }

            await context.SaveChangesAsync(cancellationToken);

            string verb = created ? "Created" : "Updated";
            if (privateKeyFingerprint is not null)
            {
                Console.WriteLine(
                    $"{verb} device '{deviceId}' with an encrypted private key (SPKI-SHA256 {privateKeyFingerprint}).");
            }

            if (ackFingerprint is not null)
            {
                Console.WriteLine(
                    $"{verb} device '{deviceId}' with an ack public key (SPKI-SHA256 {ackFingerprint}) — delivery acks enabled.");
            }

            Console.WriteLine("Restart the API if it is running — device keys are cached at load time.");
            return 0;
        }
        finally
        {
            privateKey?.Dispose();
        }
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
