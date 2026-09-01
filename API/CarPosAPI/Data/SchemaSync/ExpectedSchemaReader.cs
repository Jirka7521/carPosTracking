using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// Builds the schema the source code describes, straight from the compiled EF
/// model — the entities in <c>Data/Entities/</c> as mapped by the
/// <c>IEntityTypeConfiguration</c> classes in <c>Data/Configurations/</c>.
///
/// <para>
/// It reads the <em>relational</em> model (<c>GetRelationalModel()</c>), not the
/// entity model, because only that has been through the provider: table and column
/// names are resolved, and <c>StoreType</c> is the actual PostgreSQL type string
/// rather than a CLR type. That is what makes the result directly comparable with
/// what <see cref="DatabaseSchemaReader"/> reads out of the live catalog.
/// </para>
///
/// <para>
/// What this cannot see is anything a migration creates with raw
/// <c>migrationBuilder.Sql(...)</c> — the <c>positions.location</c> generated
/// column and its GIST index are the standing example. Those are part of the
/// source but not of the model, which is why <see cref="SchemaComparer"/> is also
/// given the migration script and never trusts "absent from the model" to mean
/// "not in the source".
/// </para>
/// </summary>
internal static class ExpectedSchemaReader
{
    /// <summary>Reads the model's tables and columns.</summary>
    /// <param name="context">Any context instance — only its model is used, so this
    /// needs no open connection and no reachable database.</param>
    /// <returns>The expected tables, keyed by table name.</returns>
    public static IReadOnlyDictionary<string, SchemaTable> Read(CarPosDbContext context)
    {
        Dictionary<string, SchemaTable> tables = new Dictionary<string, SchemaTable>(StringComparer.Ordinal);

        foreach (ITable table in context.Model.GetRelationalModel().Tables)
        {
            // Only this application's own schema. A table mapped elsewhere would be
            // compared against a catalog read that never looked there, and would
            // show up as permanently "missing".
            if (table.Schema is not null
                && !string.Equals(table.Schema, DatabaseSchemaReader.Schema, StringComparison.Ordinal))
            {
                continue;
            }

            Dictionary<string, SchemaColumn> columns = new Dictionary<string, SchemaColumn>(StringComparer.Ordinal);
            foreach (IColumn column in table.Columns)
            {
                columns[column.Name] = new SchemaColumn(
                    Name: column.Name,
                    StoreType: column.StoreType,
                    IsNullable: column.IsNullable);
            }

            tables[table.Name] = new SchemaTable(table.Name, columns);
        }

        return tables;
    }
}
