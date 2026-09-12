namespace CarPosAPI.Data.Entities;

/// <summary>
/// How much of a device's history a <see cref="ShareLink"/> exposes.
///
/// This is data minimisation made explicit: most shares exist to answer "where is
/// it right now", and answering that does not require handing over a route. The
/// creator picks per link, and <see cref="Services.Sharing.ShareViewService"/>
/// enforces the choice in SQL rather than by trimming a list afterwards.
///
/// Stored as its int value, matching <see cref="ConfigRevisionSource"/> and the
/// rest of the schema. The members below carry explicit ordinals so reordering
/// this file can never silently re-point existing rows at a different meaning —
/// which is the one real hazard of storing an enum numerically.
/// </summary>
public enum ShareScope
{
    /// <summary>
    /// Only the newest fix inside the window — one pin, refreshing. No route, no
    /// history, nothing about where the vehicle has been.
    /// </summary>
    LatestOnly = 0,

    /// <summary>
    /// Every fix inside the window, subject to the same row cap the authenticated
    /// position endpoint uses.
    /// </summary>
    FullTrack = 1,
}
