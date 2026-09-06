using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// Everything the tool knows about the source's migrations: which are applied,
/// which are pending, and what SQL each pending one would run.
///
/// <para>
/// All of this comes from EF's own runtime services — <c>IMigrator</c> and the
/// migrations assembly compiled into this application. Nothing here shells out to
/// <c>dotnet ef</c>, so the sync works on a machine with no global tool installed,
/// and the SQL shown to the operator is by construction the same SQL
/// <c>MigrateAsync</c> will run rather than a second guess at it.
/// </para>
/// </summary>
internal static class MigrationPlanner
{
    /// <summary>
    /// Statement shapes that destroy data on a table that already holds some.
    ///
    /// <para>
    /// Deliberately narrower than "anything that alters something". Dropping and
    /// re-adding a CHECK constraint — which is what raising a bound in
    /// <c>DeviceConfigRules</c> generates — destroys no rows, and flagging it would
    /// put a DATA LOSS banner on migrations that cannot lose data. That is worse
    /// than useless: a warning that cries wolf on a routine migration is a warning
    /// the operator learns to type past, and then it is not there when a real
    /// <c>DROP COLUMN</c> arrives.
    /// </para>
    /// </summary>
    private static readonly string[] DestructivePatterns =
    [
        "DROP TABLE",
        "DROP COLUMN",
        "TRUNCATE",
    ];

    /// <summary>Reads the applied and pending migration lists.</summary>
    /// <param name="context">Context connected to the target database.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Applied ids and pending ids, both in migration order.</returns>
    public static async Task<(IReadOnlyList<string> Applied, IReadOnlyList<string> Pending)> ReadStateAsync(
        CarPosDbContext context,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<string> applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        return (applied.ToList(), pending.ToList());
    }

    /// <summary>
    /// Generates the SQL for one migration, so the report can show what it does and
    /// scan it for destructive statements.
    /// </summary>
    /// <param name="context">Context (only its services are used — no connection needed).</param>
    /// <param name="migrationId">The migration to render.</param>
    /// <param name="previousMigrationId">The one before it, or <c>null</c> when it is the first.</param>
    /// <returns>The migration's SQL.</returns>
    public static string GenerateScript(CarPosDbContext context, string migrationId, string? previousMigrationId)
    {
        IMigrator migrator = context.GetService<IMigrator>();

        // fromMigration is exclusive and toMigration inclusive, so passing the
        // previous id yields exactly this one migration's statements.
        return migrator.GenerateScript(
            fromMigration: previousMigrationId ?? Migration.InitialDatabase,
            toMigration: migrationId,
            options: MigrationsSqlGenerationOptions.Default);
    }

    /// <summary>
    /// Renders the SQL of <em>every</em> migration in the source, from an empty
    /// database forward. This is not for running — it is the corpus
    /// <see cref="SchemaComparer"/> searches to tell a stray database object from
    /// one the source created with raw SQL outside the EF model.
    /// </summary>
    /// <param name="context">Context (services only).</param>
    /// <returns>The full script, or an empty string if it could not be generated.</returns>
    public static string GenerateFullScript(CarPosDbContext context)
    {
        try
        {
            IMigrator migrator = context.GetService<IMigrator>();
            return migrator.GenerateScript(
                fromMigration: Migration.InitialDatabase,
                toMigration: null,
                options: MigrationsSqlGenerationOptions.Default);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // Losing this only costs the source-owned heuristic, which fails safe:
            // with an empty script nothing is marked source-owned, and the operator
            // sees the raw difference plus its SQL before anything runs.
            return string.Empty;
        }
    }

    /// <summary>Finds the potentially destructive statements in a migration's SQL.</summary>
    /// <param name="script">SQL to scan.</param>
    /// <returns>The offending lines, trimmed, without duplicates. Whether they
    /// actually lose anything depends on the target table existing and holding rows,
    /// which only the caller can check — see
    /// <see cref="SchemaSyncPlanner"/>.</returns>
    public static IReadOnlyList<string> FindDestructiveStatements(string script)
    {
        List<string> found = new List<string>();

        foreach (string rawLine in script.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            // A type change is only lossy in the narrowing direction, which cannot be
            // read off the SQL — so every ALTER COLUMN ... TYPE counts, while the far
            // more common SET/DROP NOT NULL and DROP DEFAULT do not.
            bool isTypeChange = line.Contains("ALTER COLUMN", StringComparison.OrdinalIgnoreCase)
                && line.Contains(" TYPE ", StringComparison.OrdinalIgnoreCase);

            bool matches = isTypeChange
                || DestructivePatterns.Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            if (matches && !found.Contains(line))
            {
                found.Add(line);
            }
        }

        return found;
    }

    /// <summary>
    /// Pulls the table name out of a DDL statement, so the caller can ask whether
    /// that table exists yet and how many rows it holds. A migration that drops a
    /// column from a table it created three statements earlier destroys nothing.
    /// </summary>
    /// <param name="statement">One SQL statement.</param>
    /// <returns>The unquoted table name, or <c>null</c> when it could not be read.</returns>
    public static string? ExtractTableName(string statement)
    {
        Match match = Regex.Match(
            statement,
            @"(?:ALTER\s+TABLE|DROP\s+TABLE|TRUNCATE)\s+(?:IF\s+EXISTS\s+)?(?<name>""[^""]+""|[A-Za-z_][A-Za-z0-9_]*)(?:\s*\.\s*(?<name2>""[^""]+""|[A-Za-z_][A-Za-z0-9_]*))?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        // A qualified name puts the table in the second group; an unqualified one
        // leaves it in the first.
        string raw = match.Groups["name2"].Success ? match.Groups["name2"].Value : match.Groups["name"].Value;
        return raw.Trim('"');
    }

    /// <summary>
    /// Asks EF whether the C# model has changes no migration captures yet — the case
    /// where the comparison looks clean but the source really has moved on and
    /// somebody forgot to run <c>dotnet ef migrations add</c>.
    /// </summary>
    /// <param name="context">Context (services only).</param>
    /// <returns><c>true</c> when the model is ahead of the migrations; <c>false</c>
    /// when it is not, or when EF could not answer.</returns>
    public static bool HasModelChangesWithoutMigration(CarPosDbContext context)
    {
        try
        {
            return context.Database.HasPendingModelChanges();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // Needs the model snapshot; a project without one is not a reason to
            // fail the whole report, so the warning is simply not shown.
            return false;
        }
    }
}
