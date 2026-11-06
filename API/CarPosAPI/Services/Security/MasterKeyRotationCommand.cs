using System.Security.Cryptography;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Security;

/// <summary>
/// The <c>rotate-master-key</c> CLI mode: re-encrypts every stored device receiver
/// private key from the current master key to a new one, then exits.
///
/// <code>
/// dotnet run -- rotate-master-key --dry-run
/// dotnet run -- rotate-master-key &lt; new-master-key.txt
/// docker compose run --rm -T api rotate-master-key &lt; secrets/DeviceKeyProtection__MasterKeyBase64.new
/// </code>
///
/// <para>
/// <b>Why it exists.</b> The master key (<see cref="MasterKeyProtector"/>) is what
/// keeps a copy of the database — a backup, a dump, a stolen disk — from yielding
/// every device's private key and with it every sealed fix. Replacing it without this
/// command would make every stored key unreadable, so a key that might be exposed
/// could never safely be replaced.
/// </para>
///
/// <para>
/// <b>How it stays safe.</b> It refuses to write anything until every stored key has
/// been opened with the current master key, so a wrong current key changes nothing.
/// Each re-encrypted blob is proven to open again before it is stored
/// (<see cref="MasterKeyRewrapper"/>), and all of them are written in one
/// transaction: either every device moves to the new key, or none does.
/// </para>
///
/// <para>
/// <b>The new key arrives on standard input only</b> — never as an argument (visible
/// to every process in <c>/proc/&lt;pid&gt;/cmdline</c> and kept in shell history) and
/// never typed at a prompt (shown on screen). Run it with the API stopped: the
/// running ingest caches decrypted keys, and between this commit and the secret
/// being swapped it would hold a key that no longer matches the database.
/// </para>
///
/// Runs after the host is built but instead of starting it, like
/// <c>import-device-key</c>, so configuration and DI behave exactly as in the web
/// path. Writes to the console on purpose; prints counts and device ids, never key
/// material.
/// </summary>
internal static class MasterKeyRotationCommand
{
    /// <summary>The command word that selects this mode.</summary>
    private const string CommandName = "rotate-master-key";

    /// <summary>Checks the current key against every stored blob and changes nothing.</summary>
    private const string DryRunFlag = "--dry-run";

    /// <summary>Process exit code for any failure. Nothing has been written when it is returned.</summary>
    private const int ExitFailure = 1;

    /// <summary>Checks whether the process was started in rotation mode.</summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns><c>true</c> when the first argument is the command word.</returns>
    public static bool IsRequested(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Executes the rotation, or the dry run.</summary>
    /// <param name="services">The built application's service provider.</param>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="cancellationToken">Cancellation (Ctrl+C).</param>
    /// <returns>Process exit code.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken cancellationToken = default)
    {
        bool dryRun = args.Skip(1).Any(argument => string.Equals(argument, DryRunFlag, StringComparison.OrdinalIgnoreCase));

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IMasterKeyProtector current = scope.ServiceProvider.GetRequiredService<IMasterKeyProtector>();
        IDbContextFactory<CarPosDbContext> contextFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<CarPosDbContext>>();

        // Every stored key must open under the current master key before anything is
        // written. A wrong current key — the commonest mistake, e.g. a secret already
        // swapped by hand — therefore stops here with the database untouched.
        (int readable, List<string> unreadable) = await CheckCurrentKeyAsync(contextFactory, current, cancellationToken);

        if (unreadable.Count > 0)
        {
            await Console.Error.WriteLineAsync(
                $"{unreadable.Count} stored device key(s) do not open under the current master key: "
                + $"{string.Join(", ", unreadable)}. Nothing was changed. Check that "
                + "DeviceKeyProtection:MasterKeyBase64 is still the key they were encrypted with.");
            return ExitFailure;
        }

        if (dryRun)
        {
            Console.WriteLine(
                $"Dry run: all {readable} stored device key(s) open under the current master key. Nothing was changed.");
            return 0;
        }

        if (!Console.IsInputRedirected)
        {
            await Console.Error.WriteLineAsync(
                $"Pipe the new master key in on standard input, e.g. `{CommandName} < new-master-key.txt` "
                + $"(in Docker: `docker compose run --rm -T api {CommandName} < file`). It is never taken as an "
                + "argument or typed at a prompt, where it would land in shell history or on screen.");
            return ExitFailure;
        }

        // Trimmed: the file may end in a newline an editor added, which is not part of
        // the base64 and would otherwise make a valid key look malformed.
        string newKey = (await Console.In.ReadToEndAsync(cancellationToken)).Trim();

        DeviceKeyProtectionOptions replacementOptions = new DeviceKeyProtectionOptions { MasterKeyBase64 = newKey };

        if (!replacementOptions.HasValidMasterKey())
        {
            await Console.Error.WriteLineAsync(
                $"The new master key must be base64 of exactly {DeviceKeyProtectionOptions.MasterKeyBytes} random bytes "
                + "(e.g. `openssl rand -base64 32`). Nothing was changed.");
            return ExitFailure;
        }

        DeviceKeyProtectionOptions currentOptions =
            scope.ServiceProvider.GetRequiredService<IOptions<DeviceKeyProtectionOptions>>().Value;

        // "Rotating" to the same key would report success and protect against nothing.
        if (CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(currentOptions.MasterKeyBase64),
                Convert.FromBase64String(newKey)))
        {
            await Console.Error.WriteLineAsync("The new master key is the same as the current one. Nothing was changed.");
            return ExitFailure;
        }

        MasterKeyProtector replacement = new MasterKeyProtector(
            new OptionsWrapper<DeviceKeyProtectionOptions>(replacementOptions));

        int rewrapped = await RewrapAllAsync(contextFactory, current, replacement, cancellationToken);

        Console.WriteLine($"Re-encrypted {rewrapped} device key(s) under the new master key.");
        Console.WriteLine(
            "Now replace DeviceKeyProtection:MasterKeyBase64 (the secret file) with the new key, then start the API. "
            + "Until then the API cannot open any device key.");
        return 0;
    }

    /// <summary>Tries every stored blob against the current master key, writing nothing.</summary>
    /// <param name="contextFactory">Creates the short-lived context.</param>
    /// <param name="current">Protector over the current master key.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many opened, and the device ids of those that did not.</returns>
    private static async Task<(int Readable, List<string> Unreadable)> CheckCurrentKeyAsync(
        IDbContextFactory<CarPosDbContext> contextFactory,
        IMasterKeyProtector current,
        CancellationToken cancellationToken)
    {
        await using CarPosDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<Device> devices = await context.Devices
            .AsNoTracking()
            .Where(device => device.PrivateKeyCiphertext != null)
            .ToListAsync(cancellationToken);

        int readable = 0;
        List<string> unreadable = [];

        foreach (Device device in devices)
        {
            try
            {
                // The plaintext is discarded at once; only whether it opened matters.
                _ = current.Unprotect(device.PrivateKeyCiphertext!, device.DeviceId);
                readable++;
            }
            catch (CryptographicException)
            {
                unreadable.Add(device.DeviceId);
            }
        }

        return (readable, unreadable);
    }

    /// <summary>Re-encrypts every stored blob under the replacement key, in one transaction.</summary>
    /// <param name="contextFactory">Creates the short-lived context.</param>
    /// <param name="current">Protector over the current master key.</param>
    /// <param name="replacement">Protector over the new master key.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many blobs were re-encrypted.</returns>
    private static async Task<int> RewrapAllAsync(
        IDbContextFactory<CarPosDbContext> contextFactory,
        IMasterKeyProtector current,
        IMasterKeyProtector replacement,
        CancellationToken cancellationToken)
    {
        await using CarPosDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The connection retries transient faults (Program.cs), so the transaction has to
        // run inside the execution strategy, which may replay it whole. A replay starts
        // from a clean tracker and re-reads the rows, so it never re-encrypts twice.
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                context.ChangeTracker.Clear();

                await using IDbContextTransaction transaction =
                    await context.Database.BeginTransactionAsync(attemptToken);

                List<Device> devices = await context.Devices
                    .Where(device => device.PrivateKeyCiphertext != null)
                    .ToListAsync(attemptToken);

                foreach (Device device in devices)
                {
                    // Bound to the device id as before, so a blob still cannot be moved
                    // onto another device's row and open there.
                    device.PrivateKeyCiphertext = MasterKeyRewrapper.Rewrap(
                        current,
                        replacement,
                        device.PrivateKeyCiphertext!,
                        device.DeviceId);
                }

                await context.SaveChangesAsync(attemptToken);
                await transaction.CommitAsync(attemptToken);

                return devices.Count;
            },
            cancellationToken);
    }
}
