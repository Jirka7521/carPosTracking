namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// One difference between the live database and the source-code schema, before any
/// decision about how (or whether) to repair it. <see cref="DriftDdlGenerator"/>
/// turns these into statements; <see cref="SchemaSyncCommand"/> presents them.
/// </summary>
/// <param name="Kind">What sort of difference this is.</param>
/// <param name="Table">The table it concerns.</param>
/// <param name="Column">The column it concerns, or <c>null</c> for a whole-table difference.</param>
/// <param name="Expected">What the source model says (a store type, or a nullability word).
/// <c>null</c> when the source has nothing to say — an extra table or column.</param>
/// <param name="Actual">What the database currently has, in the same terms. <c>null</c>
/// when the database has nothing — a missing table or column.</param>
/// <param name="SourceOwnedNote">Set when the object is absent from the EF model but is
/// nonetheless created by the source's own migration SQL — the
/// <c>positions.location</c> generated column is the live example. Such a difference is
/// reported for information and is never offered as something to drop, because "not in
/// the EF model" is emphatically not the same as "not in the source".</param>
internal sealed record SchemaDifference(
    SchemaDifferenceKind Kind,
    string Table,
    string? Column,
    string? Expected,
    string? Actual,
    string? SourceOwnedNote = null)
{
    /// <summary>
    /// True when this difference must not be turned into a repair statement: the
    /// database object is the source's own doing, just not through the EF model.
    /// </summary>
    public bool IsSourceOwned => SourceOwnedNote is not null;

    /// <summary>A one-line description for the report.</summary>
    /// <returns>Human-readable text naming the object and the difference.</returns>
    public string Describe()
    {
        string target = Column is null ? Table : $"{Table}.{Column}";

        return Kind switch
        {
            SchemaDifferenceKind.MissingTable => $"table  {target,-42} missing in DB",
            SchemaDifferenceKind.ExtraTable => $"table  {target,-42} not in the source schema",
            SchemaDifferenceKind.MissingColumn => $"column {target,-42} missing in DB ({Expected})",
            SchemaDifferenceKind.ExtraColumn => $"column {target,-42} not in the source schema ({Actual})",
            SchemaDifferenceKind.TypeMismatch => $"column {target,-42} {Actual} -> {Expected}",
            SchemaDifferenceKind.NullabilityMismatch => $"column {target,-42} {Actual} -> {Expected}",
            _ => $"{Kind} {target}",
        };
    }
}
