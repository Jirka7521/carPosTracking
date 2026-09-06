namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// The kinds of difference the comparison can find between the live database and
/// the schema the source code describes. The direction is always "database
/// relative to source": <see cref="MissingTable"/> means the source has it and the
/// database does not.
/// </summary>
internal enum SchemaDifferenceKind
{
    /// <summary>In the source model, absent from the database.</summary>
    MissingTable,

    /// <summary>In the database, not in the source model — a candidate for dropping.</summary>
    ExtraTable,

    /// <summary>In the source model, absent from the database's copy of the table.</summary>
    MissingColumn,

    /// <summary>In the database's table, not in the source model — a candidate for dropping.</summary>
    ExtraColumn,

    /// <summary>Present on both sides with a different store type.</summary>
    TypeMismatch,

    /// <summary>Present on both sides, one nullable and the other not.</summary>
    NullabilityMismatch,
}
