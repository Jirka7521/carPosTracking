namespace CarPosAPI.Data.SchemaSync;

/// <summary>
/// One numbered entry in the report — the unit an operator selects or skips.
/// Everything needed to decide is on it: what it does, the exact SQL, whether it
/// destroys data, and how many rows are behind that.
/// </summary>
internal sealed class SchemaChangeItem
{
    /// <summary>Its number in the report, which is what <c>--select</c> refers to.</summary>
    public required int Number { get; init; }

    /// <summary>Whether EF's migrator or a generated statement carries this out.</summary>
    public required SchemaChangeKind Kind { get; init; }

    /// <summary>One-line summary, e.g. the migration id or the column being dropped.</summary>
    public required string Description { get; init; }

    /// <summary>Extra lines shown indented under the description — the differences a
    /// migration accounts for, or nothing for a single drift statement.</summary>
    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();

    /// <summary>The SQL this runs. Empty for a migration, whose SQL EF generates and
    /// applies itself; the report shows the migration's effect instead.</summary>
    public string? Sql { get; init; }

    /// <summary>
    /// True when applying this destroys data that cannot be recovered by re-running
    /// anything. Gated twice: the script demands the operator type <c>APPLY</c>, and
    /// the tool refuses without <c>--allow-data-loss</c> so it is equally safe by hand.
    /// </summary>
    public required bool IsDataLoss { get; init; }

    /// <summary>Rows at stake, when it could be counted — the number that actually
    /// decides whether a destructive change is acceptable.</summary>
    public long? RowCount { get; init; }

    /// <summary>
    /// Set when this change cannot be offered at all: adding a NOT NULL column to a
    /// populated table with no default, or a CREATE TABLE the model cannot fully
    /// describe. It is listed, explained and skipped rather than silently dropped
    /// from the report, because a difference nobody mentions is a difference nobody
    /// fixes.
    /// </summary>
    public string? BlockedReason { get; init; }

    /// <summary>Whether this item can be selected and applied.</summary>
    public bool IsApplicable => BlockedReason is null;
}
