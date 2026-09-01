namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// How a selectable change is carried out. The two are not interchangeable and the
/// report says so: a migration is applied whole by EF and in sequence, whereas a
/// drift statement is one generated statement that stands on its own.
/// </summary>
internal enum SchemaChangeKind
{
    /// <summary>A pending EF migration, applied by EF's own migrator.</summary>
    PendingMigration,

    /// <summary>A generated DDL statement repairing drift no migration covers.</summary>
    DriftStatement,
}
