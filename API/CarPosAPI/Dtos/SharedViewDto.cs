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
public sealed record SharedViewDto(
    ShareSessionDto Share,
    IReadOnlyList<SharedPositionDto> Positions);
