namespace CarPosAPI.Dtos;

/// <summary>
/// Everything a share page needs for one render: what the share is, and the fixes
/// it currently exposes.
///
/// <para>
/// The two travel together so that a reload — where the browser still holds the
/// share cookie but the page has forgotten everything else — is a single request
/// that either works or does not. Splitting them would mean a page that could hold
/// a valid session while failing to describe it, which is exactly the state where
/// a frontend starts guessing at bounds it should be told.
/// </para>
/// </summary>
/// <param name="Share">The share's own description, re-read from the database on every call.</param>
/// <param name="Positions">Fixes inside the window, newest first, already clamped and filtered.</param>
/// <param name="OverdueAfter">
/// When the newest fix in <paramref name="Positions"/> stops being current — the
/// instant the map's pin turns red because the tracker should have reported again by
/// then (UTC). Null when there are no fixes or the device's reporting interval is
/// unknown. The settings and the plan behind it are not sent. The threshold itself
/// (twice the longest reporting interval) can be worked out by subtracting the
/// newest fix's time. That is accepted: anyone watching the pin turn red learns it
/// anyway, and a full track shows the reporting rhythm in the fixes' spacing.
/// </param>
public sealed record SharedViewDto(
    ShareSessionDto Share,
    IReadOnlyList<SharedPositionDto> Positions,
    DateTime? OverdueAfter);
