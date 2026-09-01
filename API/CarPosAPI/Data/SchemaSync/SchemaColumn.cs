namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// One column, described the same way whether it came from the live database
/// (<see cref="DatabaseSchemaReader"/>) or from the compiled EF model
/// (<see cref="ExpectedSchemaReader"/>). Having both sides produce this identical
/// shape is what lets <see cref="SchemaComparer"/> be a plain set comparison
/// instead of a translation layer.
/// </summary>
/// <param name="Name">Column name exactly as stored (snake_case throughout this schema).</param>
/// <param name="StoreType">The PostgreSQL type with its modifiers, e.g. <c>character varying(64)</c>.
/// The live side gets this from <c>format_type()</c> rather than
/// <c>information_schema.columns.data_type</c> precisely so it is directly comparable
/// with EF's <c>IColumn.StoreType</c> — see <see cref="DatabaseSchemaReader"/>.</param>
/// <param name="IsNullable">Whether the column accepts NULL.</param>
/// <param name="IsGenerated">True for a database-generated (STORED) column. Only ever
/// true on the live side: these are created by raw SQL in a migration, so the EF model
/// does not know them and they must never be mistaken for stray columns to drop.</param>
internal sealed record SchemaColumn(
    string Name,
    string StoreType,
    bool IsNullable,
    bool IsGenerated = false);
