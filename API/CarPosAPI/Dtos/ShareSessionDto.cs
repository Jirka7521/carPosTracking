namespace CarPosAPI.Dtos;

/// <summary>
/// What a visitor is told about the share they just opened — and the boundary of
/// what they are ever told.
///
/// <para>
/// Everything identifying is absent and stays absent: no device id (the
/// case-sensitive MQTT topic the tracker publishes to), no device display name
/// unless the creator chose it as the label, no owner, no account, no other share.
/// A visitor learns that some tracker called <paramref name="Label"/> can be
/// watched between two times, and nothing else about the system it belongs to.
/// </para>
/// </summary>
/// <param name="Label">What the creator decided this tracker is called.</param>
/// <param name="ValidFrom">Start of the window (UTC) — also the earliest fix obtainable.</param>
/// <param name="ValidUntil">End of the window (UTC) — also the latest fix obtainable.</param>
/// <param name="Scope">One of <see cref="ShareScopeNames"/>, so the page knows whether to offer a range at all.</param>
/// <param name="IncludeSpeed">Whether speed will be present on each fix.</param>
/// <param name="IncludeTelemetry">Whether battery and temperature will be present on each fix.</param>
public sealed record ShareSessionDto(
    string Label,
    DateTime ValidFrom,
    DateTime ValidUntil,
    string Scope,
    bool IncludeSpeed,
    bool IncludeTelemetry);
