using Npgsql;

namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// Assembles one run of the comparison into a numbered list of changes: read both
/// schemas, diff them, work out which differences the pending migrations already
/// account for, and turn the rest into repair statements.
///
/// <para>
/// The split between the two groups matters to the operator, so it is made
/// explicitly rather than left implied. A difference a pending migration covers is
/// listed under that migration and applied by EF. A difference no migration
/// mentions is <em>drift</em> — somebody changed the database outside the migration
/// history — and gets a generated statement of its own.
/// </para>
/// </summary>
internal static class SchemaSyncPlanner
{
    /// <summary>Builds the plan.</summary>
    /// <param name="context">Context pointed at the target database.</param>
    /// <param name="connectionString">Connection string, read for the header only —
    /// the host and database name, never the password.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The assembled plan.</returns>
    public static async Task<SchemaSyncPlan> BuildAsync(
        CarPosDbContext context,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        NpgsqlConnectionStringBuilder connectionInfo = new NpgsqlConnectionStringBuilder(connectionString);

        (IReadOnlyList<string> applied, IReadOnlyList<string> pending) =
            await MigrationPlanner.ReadStateAsync(context, cancellationToken);

        string fullScript = MigrationPlanner.GenerateFullScript(context);

        IReadOnlyDictionary<string, SchemaTable> expected = ExpectedSchemaReader.Read(context);
        IReadOnlyDictionary<string, SchemaTable> actual = await DatabaseSchemaReader.ReadAsync(context, cancellationToken);
        IReadOnlyList<SchemaDifference> differences = SchemaComparer.Compare(expected, actual, fullScript);

        List<SchemaChangeItem> items = new List<SchemaChangeItem>();
        List<string> notes = new List<string>();
        int number = 1;

        // --- Pending migrations, each one item -----------------------------
        // Rendered from the migration before it, so the SQL shown is exactly this
        // migration's own statements rather than a cumulative script.
        List<string> ordered = applied.Concat(pending).ToList();
        HashSet<string> coveredIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string migrationId in pending)
        {
            int position = ordered.IndexOf(migrationId);
            string? previous = position > 0 ? ordered[position - 1] : null;
            string script = MigrationPlanner.GenerateScript(context, migrationId, previous);

            // Which of the differences above this migration accounts for. The match
            // is textual — a hint used to group and label the list, not a proof — so
            // it only ever decides presentation. Anything it mis-sorts into the drift
            // group still has its full SQL shown before it runs.
            //
            // Each difference is attributed to the FIRST pending migration that
            // mentions it and then skipped. Without that, a table that several
            // migrations happen to touch is listed under every one of them, and the
            // report stops being a list of what is wrong.
            List<string> covered = new List<string>();
            foreach (SchemaDifference difference in differences)
            {
                string key = $"{difference.Table}.{difference.Column ?? "*"}";
                if (coveredIdentifiers.Contains(key))
                {
                    continue;
                }

                string identifier = difference.Column ?? difference.Table;
                if (script.Contains(identifier, StringComparison.OrdinalIgnoreCase))
                {
                    covered.Add(difference.Describe());
                    coveredIdentifiers.Add(key);
                }
            }

            // A statement only destroys data if the table it targets exists already
            // and holds rows. On a database being built up from nothing — every
            // migration pending — the tables these statements name do not exist yet,
            // so none of them can lose anything, and saying otherwise would put a
            // DATA LOSS banner on a first-time deployment.
            (bool lossy, long? rowsAtStake, int statementCount) =
                await AssessDestructiveStatementsAsync(context, script, actual, cancellationToken);

            if (statementCount > 0 && lossy)
            {
                covered.Add($"-- includes {statementCount} statement(s) that drop or retype populated objects");
            }

            items.Add(new SchemaChangeItem
            {
                Number = number++,
                Kind = SchemaChangeKind.PendingMigration,
                Description = migrationId,
                Details = covered,
                IsDataLoss = lossy,
                RowCount = rowsAtStake,
            });
        }

        // --- Drift: whatever no pending migration accounts for --------------
        foreach (SchemaDifference difference in differences)
        {
            if (difference.IsSourceOwned)
            {
                notes.Add(
                    $"{difference.Table}.{difference.Column ?? string.Empty} is in the database but not in the EF "
                    + $"model — {difference.SourceOwnedNote}. Left alone deliberately; it is not offered for deletion.");
                continue;
            }

            if (coveredIdentifiers.Contains($"{difference.Table}.{difference.Column ?? "*"}"))
            {
                continue;
            }

            SchemaChangeItem? item = await DriftDdlGenerator.CreateAsync(
                context, difference, number, cancellationToken);

            if (item is not null)
            {
                items.Add(item);
                number++;
            }
        }

        bool inSync = items.Count == 0;
        bool hasDestructive = items.Any(static item => item.IsDataLoss && item.IsApplicable);

        SchemaSyncReport report = new SchemaSyncReport(
            Database: connectionInfo.Database ?? "(unknown)",
            Host: connectionInfo.Host ?? "(unknown)",
            InSync: inSync,
            HasDestructive: hasDestructive,
            ModelAheadOfMigrations: MigrationPlanner.HasModelChangesWithoutMigration(context),
            Items: items.Select(static item => new SchemaSyncReportItem(
                item.Number,
                item.Kind.ToString(),
                item.Description,
                item.Sql,
                item.IsDataLoss,
                item.RowCount,
                item.BlockedReason)).ToList(),
            Notes: notes);

        return new SchemaSyncPlan(items, report, pending.Count);
    }

    /// <summary>
    /// Works out whether a migration's destructive-looking statements can actually
    /// lose anything against <em>this</em> database.
    ///
    /// <para>
    /// The test is not "does the SQL contain a DROP" but "does the table it drops
    /// from exist here, with rows in it". A migration that creates a table and then
    /// alters it, or one applied to a database that has never had the table, cannot
    /// destroy data — and a gate that fires on those is a gate the operator learns
    /// to ignore before the one that matters arrives.
    /// </para>
    /// </summary>
    /// <param name="context">Context connected to the database.</param>
    /// <param name="script">The migration's SQL.</param>
    /// <param name="actual">The live schema, to test whether a target table exists.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Whether it is lossy, the largest row count at stake, and how many
    /// statements were found.</returns>
    private static async Task<(bool Lossy, long? RowsAtStake, int StatementCount)> AssessDestructiveStatementsAsync(
        CarPosDbContext context,
        string script,
        IReadOnlyDictionary<string, SchemaTable> actual,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> statements = MigrationPlanner.FindDestructiveStatements(script);
        if (statements.Count == 0)
        {
            return (false, null, 0);
        }

        bool lossy = false;
        long? rowsAtStake = null;

        foreach (string statement in statements)
        {
            string? table = MigrationPlanner.ExtractTableName(statement);

            // A name that could not be read is treated as a risk rather than waved
            // through: the whole point of the gate is that unknowns stop it.
            if (table is null)
            {
                lossy = true;
                continue;
            }

            if (!actual.ContainsKey(table))
            {
                continue;
            }

            long? rows = await DatabaseSchemaReader.CountRowsAsync(context, table, null, cancellationToken);
            if (rows is null or > 0)
            {
                lossy = true;
                if (rows is not null && (rowsAtStake is null || rows > rowsAtStake))
                {
                    rowsAtStake = rows;
                }
            }
        }

        return (lossy, rowsAtStake, statements.Count);
    }
}
