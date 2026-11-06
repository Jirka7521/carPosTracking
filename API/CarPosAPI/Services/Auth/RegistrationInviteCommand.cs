using System.Globalization;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Options;
using CarPosAPI.Services.Sharing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// The <c>create-registration-invite</c> CLI mode: mints one single-use invite code
/// for <c>POST /api/auth/register</c>, prints it once, and exits.
///
/// <code>
/// dotnet run -- create-registration-invite [--days 14]
/// docker compose run --rm api create-registration-invite --days 14
/// </code>
///
/// <para>
/// A command rather than an endpoint on purpose: there is no administrator role in
/// this API, and an HTTP way to mint codes would be one more thing to protect.
/// Whoever can run a container on the server is, by definition, the operator.
/// </para>
///
/// <para>
/// The code is printed and never stored: the table keeps only its digest
/// (<see cref="RegistrationInviteCodes"/>), so a code that is lost is simply minted
/// again. Runs after the host is built but instead of starting it, like
/// <c>import-device-key</c>.
/// </para>
/// </summary>
internal static class RegistrationInviteCommand
{
    /// <summary>The command word that selects this mode.</summary>
    private const string CommandName = "create-registration-invite";

    /// <summary>Overrides <see cref="RegistrationOptions.InviteLifetimeDays"/> for this code.</summary>
    private const string DaysArgument = "--days";

    /// <summary>The longest a code may live — the same ceiling the option itself has.</summary>
    private const int MaxDays = 365;

    /// <summary>Process exit code for any failure. Nothing has been stored when it is returned.</summary>
    private const int ExitFailure = 1;

    /// <summary>Checks whether the process was started in this mode.</summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns><c>true</c> when the first argument is the command word.</returns>
    public static bool IsRequested(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Mints and prints one invite code.</summary>
    /// <param name="services">The built application's service provider.</param>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="cancellationToken">Cancellation (Ctrl+C).</param>
    /// <returns>Process exit code.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        RegistrationOptions options = scope.ServiceProvider.GetRequiredService<IOptions<RegistrationOptions>>().Value;
        IPassphraseGenerator passphrases = scope.ServiceProvider.GetRequiredService<IPassphraseGenerator>();
        IDbContextFactory<CarPosDbContext> contextFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<CarPosDbContext>>();

        int days = options.InviteLifetimeDays;
        string? daysValue = GetArgumentValue(args, DaysArgument);

        if (daysValue is not null
            && (!int.TryParse(daysValue, NumberStyles.None, CultureInfo.InvariantCulture, out days) || days < 1 || days > MaxDays))
        {
            await Console.Error.WriteLineAsync($"Usage: {CommandName} [{DaysArgument} <1-{MaxDays}>]");
            return ExitFailure;
        }

        string code = passphrases.Generate();
        string codeHash = RegistrationInviteCodes.Hash(passphrases, code)
            ?? throw new InvalidOperationException("A freshly generated invite code normalised to nothing.");

        DateTime nowUtc = DateTime.UtcNow;
        DateTime expiresAtUtc = nowUtc.AddDays(days);

        await using CarPosDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.RegistrationInvites.Add(new RegistrationInvite
        {
            Id = Guid.NewGuid(),
            CodeHash = codeHash,
            CreatedAt = nowUtc,
            ExpiresAt = expiresAtUtc,
        });

        await context.SaveChangesAsync(cancellationToken);

        // The one time the code exists outside the recipient's hands. Not logged:
        // this is console output for the operator, and the table keeps only a digest.
        Console.WriteLine($"Invite code: {code}");
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Works for one registration until {expiresAtUtc:yyyy-MM-dd HH:mm} UTC."));
        Console.WriteLine($"Sign-up link: <dashboard address>/register?invite={code}");

        if (options.Mode != RegistrationMode.InviteOnly)
        {
            Console.WriteLine(
                $"Note: Registration:Mode is {options.Mode}, so registration does not ask for this code right now.");
        }

        return 0;
    }

    /// <summary>Reads the value following a named argument.</summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="name">The argument name (e.g. <c>--days</c>).</param>
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
