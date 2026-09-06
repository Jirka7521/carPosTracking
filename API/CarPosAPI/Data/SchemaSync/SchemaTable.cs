namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// One table and its columns, in the provider-neutral shape both readers produce.
/// Only the parts of a table this tool can meaningfully compare and repair are
/// modelled — names, types and nullability. Indexes, constraints and triggers are
/// deliberately out of scope for the comparison: EF's migrations own those, and
/// hand-generated DDL for them would be far more likely to be wrong than useful.
/// </summary>
/// <param name="Name">Table name, e.g. <c>positions</c>.</param>
/// <param name="Columns">Its columns, keyed by name for the comparer's lookups.</param>
internal sealed record SchemaTable(
    string Name,
    IReadOnlyDictionary<string, SchemaColumn> Columns);
