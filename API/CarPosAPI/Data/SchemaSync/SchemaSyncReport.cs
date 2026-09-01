namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// The machine-readable summary written to the path given by <c>--summary</c>.
/// <c>scripts/Sync-DatabaseSchema.ps1</c> reads this to decide what to prompt for —
/// which numbers exist, whether any of them destroy data, and how loudly to ask.
/// The human report on stdout is written for the operator and is not parsed.
/// </summary>
/// <param name="Database">Database name, for the report header.</param>
/// <param name="Host">Server host, for the report header.</param>
/// <param name="InSync">True when nothing differs and there is nothing to offer.</param>
/// <param name="HasDestructive">True when at least one applicable item destroys data.</param>
/// <param name="ModelAheadOfMigrations">True when the C# model has changes no migration
/// captures yet — the report can look clean while the source has genuinely moved on.</param>
/// <param name="Items">Every numbered change, applicable or blocked.</param>
/// <param name="Notes">Free-text remarks for the operator: source-owned objects that
/// were deliberately not offered for deletion, and similar.</param>
internal sealed record SchemaSyncReport(
    string Database,
    string Host,
    bool InSync,
    bool HasDestructive,
    bool ModelAheadOfMigrations,
    IReadOnlyList<SchemaSyncReportItem> Items,
    IReadOnlyList<string> Notes);
