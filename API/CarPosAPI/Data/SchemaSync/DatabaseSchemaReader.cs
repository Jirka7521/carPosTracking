using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// Reads the tables and columns a database <em>actually</em> has right now, so the
/// sync tool compares against reality rather than against what the migration
/// history claims. Everything it produces is a <see cref="SchemaTable"/>, the same
/// shape <see cref="ExpectedSchemaReader"/> builds from the source model.
///
/// <para>
/// Column types come from <c>pg_catalog</c> and <c>format_type()</c>, not from
/// <c>information_schema.columns.data_type</c>. That is not incidental:
/// <c>data_type</c> drops the modifiers (<c>character varying</c> with no length,
/// <c>numeric</c> with no precision) and reports PostGIS columns as the useless
/// <c>USER-DEFINED</c>, none of which can be compared with EF's
/// <c>IColumn.StoreType</c>. <c>format_type()</c> renders exactly the string EF
/// does — <c>character varying(64)</c>, <c>geography(Point,4326)</c> — which turns
/// type comparison into string equality instead of guesswork.
/// </para>
/// </summary>
internal static class DatabaseSchemaReader
{
    /// <summary>The schema this application owns. Nothing else is inspected or touched.</summary>
    public const string Schema = "public";

    /// <summary>
    /// Tables that live in <c>public</c> but belong to something other than this
    /// application's model, and must therefore never be reported as strays to drop.
    /// EF owns the history table; the rest are PostGIS's own bookkeeping, created by
    /// <c>CREATE EXTENSION postgis</c> (see <c>Container/Postgres/initdb/</c>).
    /// </summary>
    private static readonly HashSet<string> IgnoredTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "__EFMigrationsHistory",
        "spatial_ref_sys",
        "geography_columns",
        "geometry_columns",
        "raster_columns",
        "raster_overviews",
    };

    /// <summary>
    /// Lists the base tables in the schema. Views and PostGIS's bookkeeping are
    /// excluded — only real, application-owned tables are comparable.
    /// </summary>
    private const string TableQuery = """
        SELECT table_name
        FROM information_schema.tables
        WHERE table_schema = @schema
          AND table_type = 'BASE TABLE'
        ORDER BY table_name;
        """;

    /// <summary>
    /// Every live column with the type string EF would write, its nullability, and
    /// whether the database generates it. <c>attnum &gt; 0</c> skips the system
    /// columns; <c>NOT attisdropped</c> skips columns Postgres still keeps a slot
    /// for after a DROP.
    /// </summary>
    private const string ColumnQuery = """
        SELECT c.relname                                  AS table_name,
               a.attname                                  AS column_name,
               format_type(a.atttypid, a.atttypmod)       AS store_type,
               NOT a.attnotnull                           AS is_nullable,
               a.attgenerated <> ''                       AS is_generated
        FROM pg_catalog.pg_attribute a
        JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = @schema
          AND c.relkind = 'r'
          AND a.attnum > 0
          AND NOT a.attisdropped
        ORDER BY c.relname, a.attnum;
        """;

    /// <summary>Reads the live schema.</summary>
    /// <param name="context">Context whose connection points at the database to inspect.</param>
    /// <param name="cancellationToken">Cancellation (Ctrl+C).</param>
    /// <returns>The application-owned tables, keyed by table name.</returns>
    public static async Task<IReadOnlyDictionary<string, SchemaTable>> ReadAsync(
        CarPosDbContext context,
        CancellationToken cancellationToken = default)
    {
        DbConnection connection = context.Database.GetDbConnection();
        await OpenIfNeededAsync(connection, cancellationToken);

        List<string> tableNames = new List<string>();
        await using (DbCommand command = CreateCommand(connection, TableQuery))
        {
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string name = reader.GetString(0);
                if (!IgnoredTables.Contains(name))
                {
                    tableNames.Add(name);
                }
            }
        }

        // Columns come back for every table in one pass rather than a query per
        // table — the whole point of this tool is to be quick enough to run before
        // every deploy, and a round trip per table on a remote database is not.
        Dictionary<string, Dictionary<string, SchemaColumn>> columnsByTable =
            new Dictionary<string, Dictionary<string, SchemaColumn>>(StringComparer.Ordinal);

        await using (DbCommand command = CreateCommand(connection, ColumnQuery))
        {
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string tableName = reader.GetString(0);
                if (IgnoredTables.Contains(tableName))
                {
                    continue;
                }

                SchemaColumn column = new SchemaColumn(
                    Name: reader.GetString(1),
                    StoreType: reader.GetString(2),
                    IsNullable: reader.GetBoolean(3),
                    IsGenerated: reader.GetBoolean(4));

                if (!columnsByTable.TryGetValue(tableName, out Dictionary<string, SchemaColumn>? columns))
                {
                    columns = new Dictionary<string, SchemaColumn>(StringComparer.Ordinal);
                    columnsByTable[tableName] = columns;
                }

                columns[column.Name] = column;
            }
        }

        Dictionary<string, SchemaTable> tables = new Dictionary<string, SchemaTable>(StringComparer.Ordinal);
        foreach (string tableName in tableNames)
        {
            Dictionary<string, SchemaColumn> columns = columnsByTable.TryGetValue(tableName, out Dictionary<string, SchemaColumn>? found)
                ? found
                : new Dictionary<string, SchemaColumn>(StringComparer.Ordinal);

            tables[tableName] = new SchemaTable(tableName, columns);
        }

        return tables;
    }

    /// <summary>
    /// Counts rows in a table — what turns "this drops a column" into "this drops a
    /// column on 12904 rows", which is the number that actually decides whether an
    /// operator should go ahead.
    /// </summary>
    /// <param name="context">Context connected to the database.</param>
    /// <param name="table">Table to count.</param>
    /// <param name="column">When given, counts only rows where this column is not
    /// null — dropping a column that is null everywhere loses nothing.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The row count, or <c>null</c> when it could not be determined.</returns>
    public static async Task<long?> CountRowsAsync(
        CarPosDbContext context,
        string table,
        string? column,
        CancellationToken cancellationToken = default)
    {
        DbConnection connection = context.Database.GetDbConnection();
        await OpenIfNeededAsync(connection, cancellationToken);

        // Identifiers cannot be parameterised, so they are quoted instead. Both
        // names come from the database's own catalog, never from user input, and
        // QuoteIdentifier doubles any embedded quote — there is no path here for a
        // caller-supplied string to reach the server unquoted.
        string predicate = column is null
            ? string.Empty
            : $" WHERE {QuoteIdentifier(column)} IS NOT NULL";

        string sql = $"SELECT count(*) FROM {QuoteIdentifier(Schema)}.{QuoteIdentifier(table)}{predicate};";

        try
        {
            await using DbCommand command = CreateCommand(connection, sql, includeSchemaParameter: false);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null or DBNull ? null : Convert.ToInt64(result);
        }
        catch (DbException)
        {
            // A count is advisory. If it fails (the table was dropped underneath us,
            // or the login cannot read it) the caller still gets to see the change —
            // it simply loses the row number, which is better than losing the report.
            return null;
        }
    }

    /// <summary>Wraps an identifier in double quotes, doubling any quote inside it.</summary>
    /// <param name="identifier">Raw identifier.</param>
    /// <returns>The quoted form, safe to interpolate into SQL.</returns>
    public static string QuoteIdentifier(string identifier)
    {
        return '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    /// <summary>Opens the connection unless something already has.</summary>
    /// <param name="connection">The connection to open.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private static async Task OpenIfNeededAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    /// <summary>Builds a command, with the schema parameter bound when the SQL uses it.</summary>
    /// <param name="connection">Open connection.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="includeSchemaParameter">Whether the SQL references <c>@schema</c>.</param>
    /// <returns>The prepared command.</returns>
    private static DbCommand CreateCommand(DbConnection connection, string sql, bool includeSchemaParameter = true)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        if (includeSchemaParameter)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "schema";
            parameter.Value = Schema;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
