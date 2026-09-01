using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// Turns a difference that no pending migration covers — drift: something changed
/// the database outside the migration history — into the one statement that brings
/// it back to the source's shape.
///
/// <para>
/// Every statement it produces is printed before it runs, so the operator reviews
/// the SQL itself rather than a description of it. Where a statement cannot be made
/// safe (a NOT NULL column added to a populated table with no default; a NOT NULL
/// tightening on a column that already holds nulls) the item is returned
/// <em>blocked</em>, with the reason: listed and explained, but not offered. A
/// difference that goes unmentioned is a difference nobody fixes, so nothing is
/// ever quietly filtered out of the report.
/// </para>
///
/// <para>
/// <b>This is the fallback road, not the main one.</b> Ordinary schema evolution
/// goes through migrations, where EF generates provably correct DDL. Drift means
/// somebody changed the database by hand, and repairing it with generated SQL is
/// the pragmatic answer to a situation that should not have arisen.
/// </para>
/// </summary>
internal static class DriftDdlGenerator
{
    /// <summary>Builds the change item for one drift difference.</summary>
    /// <param name="context">Context connected to the database, for row counts.</param>
    /// <param name="difference">The difference to repair.</param>
    /// <param name="number">The item's number in the report.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The item, or <c>null</c> when the difference needs no action.</returns>
    public static async Task<SchemaChangeItem?> CreateAsync(
        CarPosDbContext context,
        SchemaDifference difference,
        int number,
        CancellationToken cancellationToken = default)
    {
        // Source-owned objects are reported by the caller and never repaired: the
        // source created them, just not through the EF model.
        if (difference.IsSourceOwned)
        {
            return null;
        }

        string table = DatabaseSchemaReader.QuoteIdentifier(difference.Table);
        string qualified = $"{DatabaseSchemaReader.QuoteIdentifier(DatabaseSchemaReader.Schema)}.{table}";

        return difference.Kind switch
        {
            SchemaDifferenceKind.ExtraColumn => await CreateDropColumnAsync(
                context, difference, number, qualified, cancellationToken),

            SchemaDifferenceKind.ExtraTable => await CreateDropTableAsync(
                context, difference, number, qualified, cancellationToken),

            SchemaDifferenceKind.MissingColumn => await CreateAddColumnAsync(
                context, difference, number, qualified, cancellationToken),

            SchemaDifferenceKind.TypeMismatch => await CreateAlterTypeAsync(
                context, difference, number, qualified, cancellationToken),

            SchemaDifferenceKind.NullabilityMismatch => await CreateAlterNullabilityAsync(
                context, difference, number, qualified, cancellationToken),

            // A table missing from a database that has some of the schema means it
            // was dropped outside migrations. Rebuilding it from relational metadata
            // alone would miss whatever raw SQL its migration also ran (the
            // positions.location generated column is exactly such a case), so this
            // points at the migration path instead of guessing.
            SchemaDifferenceKind.MissingTable => new SchemaChangeItem
            {
                Number = number,
                Kind = SchemaChangeKind.DriftStatement,
                Description = $"table {difference.Table} is missing and no pending migration creates it",
                IsDataLoss = false,
                BlockedReason =
                    "the table was dropped outside the migration history. Re-create it by re-running its "
                    + "migration against an empty database, or add a new migration — a CREATE TABLE built "
                    + "from the model alone would omit anything its migration created with raw SQL.",
            },

            _ => null,
        };
    }

    /// <summary>Drops a column the source does not have. Destructive.</summary>
    /// <param name="context">Context, for the row count.</param>
    /// <param name="difference">The extra column.</param>
    /// <param name="number">Item number.</param>
    /// <param name="qualified">Schema-qualified, quoted table name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The change item.</returns>
    private static async Task<SchemaChangeItem> CreateDropColumnAsync(
        CarPosDbContext context,
        SchemaDifference difference,
        int number,
        string qualified,
        CancellationToken cancellationToken)
    {
        string column = DatabaseSchemaReader.QuoteIdentifier(difference.Column!);

        // Counting only the non-null rows is the honest number: dropping a column
        // that is null everywhere destroys nothing, and saying "12904 rows" there
        // would train the operator to ignore the warning that matters.
        long? rows = await DatabaseSchemaReader.CountRowsAsync(
            context, difference.Table, difference.Column, cancellationToken);

        return new SchemaChangeItem
        {
            Number = number,
            Kind = SchemaChangeKind.DriftStatement,
            Description = $"drop column {difference.Table}.{difference.Column} ({difference.Actual})",
            Sql = $"ALTER TABLE {qualified} DROP COLUMN {column};",
            IsDataLoss = rows is null or > 0,
            RowCount = rows,
        };
    }

    /// <summary>Drops a table the source does not have. Destructive.</summary>
    /// <param name="context">Context, for the row count.</param>
    /// <param name="difference">The extra table.</param>
    /// <param name="number">Item number.</param>
    /// <param name="qualified">Schema-qualified, quoted table name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The change item.</returns>
    private static async Task<SchemaChangeItem> CreateDropTableAsync(
        CarPosDbContext context,
        SchemaDifference difference,
        int number,
        string qualified,
        CancellationToken cancellationToken)
    {
        long? rows = await DatabaseSchemaReader.CountRowsAsync(
            context, difference.Table, null, cancellationToken);

        return new SchemaChangeItem
        {
            Number = number,
            Kind = SchemaChangeKind.DriftStatement,
            Description = $"drop table {difference.Table}",
            Sql = $"DROP TABLE {qualified};",
            IsDataLoss = rows is null or > 0,
            RowCount = rows,
        };
    }

    /// <summary>
    /// Adds a column the source has. Safe when nullable; blocked when the source
    /// wants NOT NULL, the table has rows and the model offers no default — such a
    /// statement would simply fail, and emitting SQL known to fail helps nobody.
    /// </summary>
    /// <param name="context">Context, for the row count.</param>
    /// <param name="difference">The missing column.</param>
    /// <param name="number">Item number.</param>
    /// <param name="qualified">Schema-qualified, quoted table name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The change item.</returns>
    private static async Task<SchemaChangeItem> CreateAddColumnAsync(
        CarPosDbContext context,
        SchemaDifference difference,
        int number,
        string qualified,
        CancellationToken cancellationToken)
    {
        string column = DatabaseSchemaReader.QuoteIdentifier(difference.Column!);
        SchemaColumn? expected = FindExpectedColumn(context, difference);
        bool isNullable = expected?.IsNullable ?? true;
        string storeType = expected?.StoreType ?? difference.Expected ?? "text";

        if (!isNullable)
        {
            long? rows = await DatabaseSchemaReader.CountRowsAsync(
                context, difference.Table, null, cancellationToken);

            if (rows is null or > 0)
            {
                return new SchemaChangeItem
                {
                    Number = number,
                    Kind = SchemaChangeKind.DriftStatement,
                    Description = $"add column {difference.Table}.{difference.Column} ({storeType} NOT NULL)",
                    Sql = $"ALTER TABLE {qualified} ADD COLUMN {column} {storeType} NOT NULL;",
                    IsDataLoss = false,
                    RowCount = rows,
                    BlockedReason =
                        $"the source makes this column NOT NULL and {DescribeRows(rows)} would have no value "
                        + "for it. Add it through a migration that supplies a default or backfills the "
                        + "existing rows — this tool will not invent the value.",
                };
            }
        }

        return new SchemaChangeItem
        {
            Number = number,
            Kind = SchemaChangeKind.DriftStatement,
            Description = $"add column {difference.Table}.{difference.Column} ({storeType})",
            Sql = $"ALTER TABLE {qualified} ADD COLUMN {column} {storeType}{(isNullable ? " NULL" : " NOT NULL")};",
            IsDataLoss = false,
        };
    }

    /// <summary>Changes a column's type. Flagged, because a narrowing cast truncates.</summary>
    /// <param name="context">Context, for the row count.</param>
    /// <param name="difference">The type mismatch.</param>
    /// <param name="number">Item number.</param>
    /// <param name="qualified">Schema-qualified, quoted table name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The change item.</returns>
    private static async Task<SchemaChangeItem> CreateAlterTypeAsync(
        CarPosDbContext context,
        SchemaDifference difference,
        int number,
        string qualified,
        CancellationToken cancellationToken)
    {
        string column = DatabaseSchemaReader.QuoteIdentifier(difference.Column!);
        string storeType = difference.Expected!;

        long? rows = await DatabaseSchemaReader.CountRowsAsync(
            context, difference.Table, difference.Column, cancellationToken);

        return new SchemaChangeItem
        {
            Number = number,
            Kind = SchemaChangeKind.DriftStatement,
            Description = $"change {difference.Table}.{difference.Column} from {difference.Actual} to {storeType}",
            Sql = $"ALTER TABLE {qualified} ALTER COLUMN {column} TYPE {storeType} USING {column}::{storeType};",

            // Always flagged: whether the cast loses anything depends on the values,
            // not the types (double precision -> real silently rounds every row).
            // The operator knows their data; the tool only guarantees they are asked.
            IsDataLoss = rows is null or > 0,
            RowCount = rows,
        };
    }

    /// <summary>
    /// Adds or removes NOT NULL. Tightening is blocked when nulls already exist,
    /// because the statement would fail — better to say why up front.
    /// </summary>
    /// <param name="context">Context, for the null count.</param>
    /// <param name="difference">The nullability mismatch.</param>
    /// <param name="number">Item number.</param>
    /// <param name="qualified">Schema-qualified, quoted table name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The change item.</returns>
    private static async Task<SchemaChangeItem> CreateAlterNullabilityAsync(
        CarPosDbContext context,
        SchemaDifference difference,
        int number,
        string qualified,
        CancellationToken cancellationToken)
    {
        string column = DatabaseSchemaReader.QuoteIdentifier(difference.Column!);
        bool wantsNullable = string.Equals(difference.Expected, "nullable", StringComparison.Ordinal);

        if (wantsNullable)
        {
            return new SchemaChangeItem
            {
                Number = number,
                Kind = SchemaChangeKind.DriftStatement,
                Description = $"allow NULL in {difference.Table}.{difference.Column}",
                Sql = $"ALTER TABLE {qualified} ALTER COLUMN {column} DROP NOT NULL;",
                IsDataLoss = false,
            };
        }

        long? total = await DatabaseSchemaReader.CountRowsAsync(
            context, difference.Table, null, cancellationToken);
        long? populated = await DatabaseSchemaReader.CountRowsAsync(
            context, difference.Table, difference.Column, cancellationToken);

        bool hasNulls = total is not null && populated is not null && total > populated;

        return new SchemaChangeItem
        {
            Number = number,
            Kind = SchemaChangeKind.DriftStatement,
            Description = $"require NOT NULL on {difference.Table}.{difference.Column}",
            Sql = $"ALTER TABLE {qualified} ALTER COLUMN {column} SET NOT NULL;",
            IsDataLoss = false,
            RowCount = total,
            BlockedReason = hasNulls
                ? $"{total - populated} existing row(s) hold NULL in this column, so SET NOT NULL would fail. "
                  + "Backfill them first, then re-run."
                : null,
        };
    }

    /// <summary>Finds the model's version of a column, for its type and nullability.</summary>
    /// <param name="context">Context whose model is consulted.</param>
    /// <param name="difference">The difference naming the table and column.</param>
    /// <returns>The expected column, or <c>null</c> if the model does not have it.</returns>
    private static SchemaColumn? FindExpectedColumn(CarPosDbContext context, SchemaDifference difference)
    {
        IReadOnlyDictionary<string, SchemaTable> expected = ExpectedSchemaReader.Read(context);

        if (expected.TryGetValue(difference.Table, out SchemaTable? table)
            && difference.Column is not null
            && table.Columns.TryGetValue(difference.Column, out SchemaColumn? column))
        {
            return column;
        }

        return null;
    }

    /// <summary>Phrases a row count for a message, coping with an unknown one.</summary>
    /// <param name="rows">The count, or <c>null</c> when it could not be read.</param>
    /// <returns>Text to drop into a sentence.</returns>
    private static string DescribeRows(long? rows)
    {
        return rows is null ? "the existing rows" : $"{rows} existing row(s)";
    }

    /// <summary>Runs one generated statement in its own transaction.</summary>
    /// <param name="context">Context connected to the database.</param>
    /// <param name="sql">The statement to run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task ExecuteAsync(CarPosDbContext context, string sql, CancellationToken cancellationToken = default)
    {
        DbConnection connection = context.Database.GetDbConnection();

        // In practice the planner's catalog read has already opened this, but an
        // apply must not depend on the order some earlier step happened to run in.
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // Its own transaction, so a failure half way through a selection leaves the
        // earlier statements committed and this one entirely undone — the caller
        // stops and reports exactly which statement failed.
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
