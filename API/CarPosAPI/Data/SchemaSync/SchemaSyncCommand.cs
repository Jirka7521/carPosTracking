using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// The <c>schema-sync</c> CLI mode:
/// <c>dotnet run -- schema-sync report --connection "&lt;conn&gt;" [--summary &lt;path&gt;]</c> and
/// <c>dotnet run -- schema-sync apply  --connection "&lt;conn&gt;" --select 1,2,4
/// [--verify &lt;summary.json&gt;] [--allow-data-loss]</c>.
///
/// <para>
/// Reads what the target database actually contains, compares it with the schema
/// the source code describes, and reports every difference as a numbered, individually
/// selectable change. Nothing is ever applied that was not listed, chosen and confirmed —
/// the project's rule is that migrations are reviewed before they are applied and a
/// production database is never auto-migrated, and this tool exists to make that review
/// possible rather than to replace it.
/// </para>
///
/// <para>
/// Unlike <c>import-device-key</c>, this runs <em>before</em> the host is built. It must
/// work without the JWT key, master key or broker credentials (whose <c>ValidateOnStart</c>
/// would otherwise refuse to boot for a task that touches none of them), and it must be
/// able to point at any database — so it builds its own context from <c>--connection</c>
/// instead of taking one from DI, which also sidesteps <c>appsettings.Local.json</c> being
/// loaded last and overriding anything passed in.
/// </para>
///
/// <para>
/// This class writes to the console deliberately: it is an interactive command, not
/// service code. It never prints the connection string, which carries a password.
/// </para>
/// </summary>
internal static class SchemaSyncCommand
{
    /// <summary>The command word that selects this mode.</summary>
    private const string CommandName = "schema-sync";

    /// <summary>Process exit code for a failure.</summary>
    private const int ExitFailure = 1;

    /// <summary>Process exit code meaning "differences found" — not an error.</summary>
    private const int ExitDifferencesFound = 2;

    /// <summary>Checks whether the process was started in schema-sync mode.</summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns><c>true</c> when the first argument is the command word.</returns>
    public static bool IsRequested(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs the report or the apply.</summary>
    /// <param name="args">Raw command-line arguments (parsed by hand — the positional
    /// command word makes IConfiguration mapping unreliable, as in the import mode).</param>
    /// <param name="cancellationToken">Cancellation (Ctrl+C).</param>
    /// <returns>Process exit code: 0 in sync / applied, 2 differences found, 1 failure.</returns>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        string subcommand = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        string? connectionString = GetArgumentValue(args, "--connection");
        string? summaryPath = GetArgumentValue(args, "--summary");
        string? selection = GetArgumentValue(args, "--select");
        string? verifyPath = GetArgumentValue(args, "--verify");
        bool allowDataLoss = args.Contains("--allow-data-loss", StringComparer.OrdinalIgnoreCase);

        if (subcommand is not ("report" or "apply") || string.IsNullOrWhiteSpace(connectionString))
        {
            await Console.Error.WriteLineAsync(
                $"Usage: dotnet run -- {CommandName} report --connection \"<conn>\" [--summary <path>]");
            await Console.Error.WriteLineAsync(
                $"       dotnet run -- {CommandName} apply  --connection \"<conn>\" --select <numbers|all> "
                + "[--verify <summary.json>] [--allow-data-loss]");
            return ExitFailure;
        }

        DbContextOptionsBuilder<CarPosDbContext> optionsBuilder = new DbContextOptionsBuilder<CarPosDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        await using CarPosDbContext context = new CarPosDbContext(optionsBuilder.Options);

        try
        {
            return subcommand switch
            {
                "report" => await RunReportAsync(context, connectionString, summaryPath, cancellationToken),
                "apply" => await RunApplyAsync(context, connectionString, selection, verifyPath, allowDataLoss, cancellationToken),
                _ => ExitFailure,
            };
        }
        catch (NpgsqlException exception)
        {
            // The overwhelmingly common failures are "cannot reach the server" and
            // "permission denied for ..." (the DML-only BE role attempting DDL).
            // Both deserve the plain message, not a stack trace.
            await Console.Error.WriteLineAsync($"Database error: {exception.Message}");
            return ExitFailure;
        }
    }

    /// <summary>Builds the plan and prints it.</summary>
    /// <param name="context">Context pointed at the target database.</param>
    /// <param name="connectionString">Connection string, for the header only.</param>
    /// <param name="summaryPath">Where to write the JSON summary, if anywhere.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>0 when in sync, 2 when differences were found.</returns>
    private static async Task<int> RunReportAsync(
        CarPosDbContext context,
        string connectionString,
        string? summaryPath,
        CancellationToken cancellationToken)
    {
        SchemaSyncPlan plan = await SchemaSyncPlanner.BuildAsync(context, connectionString, cancellationToken);
        PrintPlan(plan);

        if (summaryPath is not null)
        {
            await WriteSummaryAsync(plan, summaryPath, cancellationToken);
        }

        return plan.Report.InSync ? 0 : ExitDifferencesFound;
    }

    /// <summary>Applies the selected changes.</summary>
    /// <param name="context">Context pointed at the target database.</param>
    /// <param name="connectionString">Connection string, for the header only.</param>
    /// <param name="selection">The <c>--select</c> value: numbers, or "all".</param>
    /// <param name="verifyPath">The summary the operator actually confirmed, if given.</param>
    /// <param name="allowDataLoss">Whether destructive items may run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>0 on success, 1 on refusal or failure.</returns>
    private static async Task<int> RunApplyAsync(
        CarPosDbContext context,
        string connectionString,
        string? selection,
        string? verifyPath,
        bool allowDataLoss,
        CancellationToken cancellationToken)
    {
        // The plan is rebuilt rather than carried over from the report run: the
        // database may have moved in between, and applying a stale plan is exactly
        // how a tool like this destroys something nobody agreed to.
        SchemaSyncPlan plan = await SchemaSyncPlanner.BuildAsync(context, connectionString, cancellationToken);

        if (plan.Report.InSync)
        {
            Console.WriteLine("Nothing to apply — the database already matches the source schema.");
            return 0;
        }

        // Rebuilding the plan is what makes it current, but it also means the
        // numbers could now mean something else than they did in the report the
        // operator said yes to. --verify closes that: the confirmed report is
        // compared against the fresh one, and anything that moved stops the run.
        if (verifyPath is not null && !await VerifyAgainstConfirmedReportAsync(plan, verifyPath, selection, cancellationToken))
        {
            return ExitFailure;
        }

        IReadOnlyList<SchemaChangeItem>? selected = ResolveSelection(plan, selection);
        if (selected is null)
        {
            return ExitFailure;
        }

        if (selected.Count == 0)
        {
            Console.WriteLine("Nothing selected — no changes made.");
            return 0;
        }

        // Second gate behind the script's typed confirmation, so running the tool by
        // hand is exactly as safe as running it through the menu.
        SchemaChangeItem[] destructive = selected.Where(static item => item.IsDataLoss).ToArray();
        if (destructive.Length > 0 && !allowDataLoss)
        {
            await Console.Error.WriteLineAsync("Refusing: the selection destroys data and --allow-data-loss was not given.");
            foreach (SchemaChangeItem item in destructive)
            {
                await Console.Error.WriteLineAsync($"  [{item.Number}] {item.Description}{FormatRows(item)}");
            }

            return ExitFailure;
        }

        // Migrations first and in order. EF applies them as a sequence, so this must
        // happen before any generated statement that might depend on the result.
        SchemaChangeItem[] migrations = selected
            .Where(static item => item.Kind == SchemaChangeKind.PendingMigration)
            .OrderBy(static item => item.Number)
            .ToArray();

        if (migrations.Length > 0)
        {
            if (!ValidateMigrationSelection(plan, migrations))
            {
                return ExitFailure;
            }

            Console.WriteLine();
            Console.WriteLine($"Applying {migrations.Length} migration(s)...");
            foreach (SchemaChangeItem migration in migrations)
            {
                Console.WriteLine($"  {migration.Description}");
            }

            // Migrate to the LAST selected migration, not simply "everything
            // pending". DbContext.MigrateAsync() would apply the whole pending set,
            // so picking the first two of five would quietly run all five — the
            // opposite of what a per-item selection is for. IMigrator takes an
            // explicit target, and ValidateMigrationSelection above has already
            // established that the selection runs contiguously up to it.
            string target = migrations[^1].Description;
            await context.GetService<IMigrator>().MigrateAsync(target, cancellationToken);
            Console.WriteLine("Migrations applied.");
        }

        SchemaChangeItem[] driftStatements = selected
            .Where(static item => item.Kind == SchemaChangeKind.DriftStatement && item.Sql is not null)
            .OrderBy(static item => item.Number)
            .ToArray();

        foreach (SchemaChangeItem item in driftStatements)
        {
            Console.WriteLine();
            Console.WriteLine($"  {item.Sql}");
            try
            {
                await DriftDdlGenerator.ExecuteAsync(context, item.Sql!, cancellationToken);
                Console.WriteLine("  ok");
            }
            catch (NpgsqlException exception)
            {
                await Console.Error.WriteLineAsync($"  FAILED: {exception.Message}");
                await Console.Error.WriteLineAsync("  Stopping — earlier changes stand, this one was rolled back.");
                return ExitFailure;
            }
        }

        // Re-read from scratch so the closing summary reflects the database as it now
        // is, not as the tool believes it left it.
        Console.WriteLine();
        Console.WriteLine("Re-checking...");
        await using CarPosDbContext verification = NewContext(connectionString);
        SchemaSyncPlan after = await SchemaSyncPlanner.BuildAsync(verification, connectionString, cancellationToken);

        if (after.Report.InSync)
        {
            Console.WriteLine("The database now matches the source schema.");
            return 0;
        }

        Console.WriteLine("Still different after applying:");
        PrintPlan(after);
        return 0;
    }

    /// <summary>
    /// Refuses a migration selection that skips an earlier pending one. EF applies
    /// migrations in sequence — there is no way to run the third without the first
    /// two — so a selection that implies more than it names is rejected with the
    /// names, rather than quietly applying migrations the operator did not pick.
    /// </summary>
    /// <param name="plan">The current plan.</param>
    /// <param name="selectedMigrations">The migration items chosen.</param>
    /// <returns><c>true</c> when the selection is contiguous from the first pending one.</returns>
    private static bool ValidateMigrationSelection(SchemaSyncPlan plan, IReadOnlyList<SchemaChangeItem> selectedMigrations)
    {
        SchemaChangeItem[] allMigrations = plan.Items
            .Where(static item => item.Kind == SchemaChangeKind.PendingMigration)
            .OrderBy(static item => item.Number)
            .ToArray();

        int lastSelected = selectedMigrations.Max(static item => item.Number);
        SchemaChangeItem[] implied = allMigrations
            .Where(item => item.Number <= lastSelected && selectedMigrations.All(chosen => chosen.Number != item.Number))
            .ToArray();

        if (implied.Length == 0)
        {
            return true;
        }

        Console.Error.WriteLine("Refusing: EF applies migrations in order, so this selection would also apply:");
        foreach (SchemaChangeItem item in implied)
        {
            Console.Error.WriteLine($"  [{item.Number}] {item.Description}");
        }

        Console.Error.WriteLine("Select those too, or choose an earlier migration.");
        return false;
    }

    /// <summary>
    /// Checks that the changes the operator confirmed are still the changes these
    /// numbers name. Between the report and the apply the database can move — a
    /// colleague runs a migration, an ingest writes rows — and the plan is rebuilt
    /// each time, so number 2 is not guaranteed to be the same thing it was. Without
    /// this check that could silently turn a confirmed "add a column" into a
    /// "drop a column", which is the one failure this whole tool exists to prevent.
    /// </summary>
    /// <param name="plan">The freshly built plan.</param>
    /// <param name="verifyPath">Path of the summary written by the report the
    /// operator confirmed.</param>
    /// <param name="selection">The selection being applied.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns><c>true</c> when the selected numbers still mean the same changes.</returns>
    private static async Task<bool> VerifyAgainstConfirmedReportAsync(
        SchemaSyncPlan plan,
        string verifyPath,
        string? selection,
        CancellationToken cancellationToken)
    {
        SchemaSyncReport? confirmed;
        try
        {
            string json = await File.ReadAllTextAsync(verifyPath, cancellationToken);
            confirmed = JsonSerializer.Deserialize<SchemaSyncReport>(json);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            await Console.Error.WriteLineAsync($"Could not read the confirmed report at {verifyPath}: {exception.Message}");
            return false;
        }

        if (confirmed is null)
        {
            await Console.Error.WriteLineAsync($"The confirmed report at {verifyPath} was empty.");
            return false;
        }

        // Only the numbers actually being applied need to still match. A change
        // elsewhere in the list is not a reason to refuse — it is reported by the
        // re-check at the end.
        IEnumerable<int> numbers = string.Equals(selection?.Trim(), "all", StringComparison.OrdinalIgnoreCase)
            ? confirmed.Items.Where(static item => item.BlockedReason is null).Select(static item => item.Number)
            : (selection ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => int.TryParse(part, out int value) ? value : -1);

        foreach (int number in numbers)
        {
            SchemaSyncReportItem? was = confirmed.Items.FirstOrDefault(item => item.Number == number);
            SchemaChangeItem? now = plan.Items.FirstOrDefault(item => item.Number == number);

            bool same = was is not null
                && now is not null
                && string.Equals(was.Description, now.Description, StringComparison.Ordinal)
                && string.Equals(was.Sql, now.Sql, StringComparison.Ordinal)
                && was.IsDataLoss == now.IsDataLoss;

            if (!same)
            {
                await Console.Error.WriteLineAsync(
                    "Refusing: the database changed since the report you confirmed, so these numbers no longer "
                    + "mean the same changes.");
                await Console.Error.WriteLineAsync($"  [{number}] was: {was?.Description ?? "(not in the report)"}");
                await Console.Error.WriteLineAsync($"  [{number}] now: {now?.Description ?? "(no longer present)"}");
                await Console.Error.WriteLineAsync("Re-run the comparison and choose again.");
                return false;
            }
        }

        return true;
    }

    /// <summary>Turns the <c>--select</c> value into the items it names.</summary>
    /// <param name="plan">The current plan.</param>
    /// <param name="selection">"all", or a comma-separated list of numbers.</param>
    /// <returns>The chosen items, or <c>null</c> when the value was not usable.</returns>
    private static IReadOnlyList<SchemaChangeItem>? ResolveSelection(SchemaSyncPlan plan, string? selection)
    {
        SchemaChangeItem[] applicable = plan.Items.Where(static item => item.IsApplicable).ToArray();

        if (string.IsNullOrWhiteSpace(selection))
        {
            Console.Error.WriteLine("Nothing selected: pass --select with numbers from the report, or --select all.");
            return null;
        }

        if (string.Equals(selection.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return applicable;
        }

        List<SchemaChangeItem> chosen = new List<SchemaChangeItem>();
        foreach (string part in selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out int number))
            {
                Console.Error.WriteLine($"Not a number in --select: '{part}'.");
                return null;
            }

            SchemaChangeItem? item = plan.Items.FirstOrDefault(candidate => candidate.Number == number);
            if (item is null)
            {
                Console.Error.WriteLine($"No change numbered {number} in the report.");
                return null;
            }

            if (!item.IsApplicable)
            {
                Console.Error.WriteLine($"Change {number} cannot be applied: {item.BlockedReason}");
                return null;
            }

            chosen.Add(item);
        }

        return chosen;
    }

    /// <summary>Prints the human-readable report.</summary>
    /// <param name="plan">The plan to print.</param>
    private static void PrintPlan(SchemaSyncPlan plan)
    {
        Console.WriteLine();
        Console.WriteLine($"Database  {plan.Report.Database} @ {plan.Report.Host}");
        Console.WriteLine($"Source    API/CarPosAPI  ({plan.PendingMigrationCount} pending migration(s))");
        Console.WriteLine();

        if (plan.Report.InSync)
        {
            Console.WriteLine("In sync — the database matches the schema the source code describes.");
            PrintNotes(plan);
            return;
        }

        SchemaChangeItem[] migrations = plan.Items
            .Where(static item => item.Kind == SchemaChangeKind.PendingMigration)
            .ToArray();

        if (migrations.Length > 0)
        {
            Console.WriteLine("Pending migrations — applied by EF, each whole and in order:");
            foreach (SchemaChangeItem item in migrations)
            {
                PrintItem(item);
            }

            Console.WriteLine();
        }

        SchemaChangeItem[] drift = plan.Items
            .Where(static item => item.Kind == SchemaChangeKind.DriftStatement)
            .ToArray();

        if (drift.Length > 0)
        {
            Console.WriteLine("Drift — in the database but not in any migration, repaired by generated DDL:");
            foreach (SchemaChangeItem item in drift)
            {
                PrintItem(item);
            }

            Console.WriteLine();
        }

        PrintNotes(plan);
    }

    /// <summary>Prints one numbered item with its SQL and any warning.</summary>
    /// <param name="item">The item to print.</param>
    private static void PrintItem(SchemaChangeItem item)
    {
        Console.WriteLine($"  [{item.Number}]  {item.Description}");

        foreach (string detail in item.Details)
        {
            Console.WriteLine($"           {detail}");
        }

        if (item.Sql is not null)
        {
            Console.WriteLine($"           {item.Sql}");
        }

        if (item.BlockedReason is not null)
        {
            Console.WriteLine($"           -- CANNOT APPLY: {item.BlockedReason}");
            return;
        }

        if (item.IsDataLoss)
        {
            Console.WriteLine($"           !! DATA LOSS{FormatRows(item)}");
        }
    }

    /// <summary>Prints the closing notes and warnings.</summary>
    /// <param name="plan">The plan whose notes to print.</param>
    private static void PrintNotes(SchemaSyncPlan plan)
    {
        foreach (string note in plan.Report.Notes)
        {
            Console.WriteLine($"note: {note}");
        }

        if (plan.Report.ModelAheadOfMigrations)
        {
            Console.WriteLine();
            Console.WriteLine(
                "warning: the C# model has changes no migration captures yet. Even a clean report above "
                + "does not mean the database matches the code — run `dotnet ef migrations add <Name>` first.");
        }
    }

    /// <summary>Formats the row count clause of a warning.</summary>
    /// <param name="item">The item whose rows to describe.</param>
    /// <returns>Text such as " — 12904 row(s) affected", or a note that it is unknown.</returns>
    private static string FormatRows(SchemaChangeItem item)
    {
        return item.RowCount is null
            ? " — row count unavailable, assume data is at stake"
            : $" — {item.RowCount} row(s) affected";
    }

    /// <summary>Writes the JSON summary the PowerShell driver reads.</summary>
    /// <param name="plan">The plan to serialise.</param>
    /// <param name="path">Destination file.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private static async Task WriteSummaryAsync(SchemaSyncPlan plan, string path, CancellationToken cancellationToken)
    {
        JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(plan.Report, options);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    /// <summary>Creates a second context on the same database, for the post-apply re-check.</summary>
    /// <param name="connectionString">The connection to use.</param>
    /// <returns>A fresh context.</returns>
    private static CarPosDbContext NewContext(string connectionString)
    {
        DbContextOptionsBuilder<CarPosDbContext> optionsBuilder = new DbContextOptionsBuilder<CarPosDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new CarPosDbContext(optionsBuilder.Options);
    }

    /// <summary>Reads the value following a named argument.</summary>
    /// <param name="args">Raw arguments.</param>
    /// <param name="name">The flag to find, e.g. <c>--connection</c>.</param>
    /// <returns>The following argument, or <c>null</c> when absent or last.</returns>
    private static string? GetArgumentValue(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
