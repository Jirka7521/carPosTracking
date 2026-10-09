namespace CarPosAPI.Dtos;

/// <summary>
/// The caller's standing agreement to load the Google map, as
/// <c>GET</c> and <c>PUT /api/me/maps-consent</c> return it.
///
/// A separate shape rather than two more members on <see cref="UserProfileDto"/>:
/// that one is handed to <em>other</em> users too (the sharing list, the email
/// lookup), and whether somebody lets Google see their map is nobody else's
/// business.
/// </summary>
/// <param name="Version">The prompt version agreed to, or null when there is no standing agreement.</param>
/// <param name="GrantedAt">When it was agreed to (UTC), or null alongside <paramref name="Version"/>.</param>
public sealed record MapsConsentDto(
    string? Version,
    DateTime? GrantedAt);
