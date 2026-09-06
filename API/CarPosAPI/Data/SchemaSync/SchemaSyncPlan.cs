namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// The assembled result of one comparison: every numbered change plus the summary
/// that describes it. Built by <see cref="SchemaSyncPlanner"/>, printed and applied
/// by <see cref="SchemaSyncCommand"/>.
/// </summary>
/// <param name="Items">The numbered changes, migrations first then drift.</param>
/// <param name="Report">The machine-readable summary of the same thing.</param>
/// <param name="PendingMigrationCount">How many migrations are pending, for the header.</param>
internal sealed record SchemaSyncPlan(
    IReadOnlyList<SchemaChangeItem> Items,
    SchemaSyncReport Report,
    int PendingMigrationCount);
