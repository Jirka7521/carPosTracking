namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// One selectable change, flattened for the JSON summary that
/// <c>scripts/Sync-DatabaseSchema.ps1</c> reads. The script drives its prompts off
/// these fields rather than parsing the human report — prose is for the operator,
/// this is the contract.
/// </summary>
/// <param name="Number">The number shown in the report and accepted by <c>--select</c>.</param>
/// <param name="Kind">"PendingMigration" or "DriftStatement".</param>
/// <param name="Description">The one-line summary.</param>
/// <param name="Sql">The statement, for a drift item; null for a migration.</param>
/// <param name="IsDataLoss">Whether applying it destroys data.</param>
/// <param name="RowCount">Rows at stake, when known.</param>
/// <param name="BlockedReason">Why it cannot be applied, when it cannot.</param>
internal sealed record SchemaSyncReportItem(
    int Number,
    string Kind,
    string Description,
    string? Sql,
    bool IsDataLoss,
    long? RowCount,
    string? BlockedReason);
