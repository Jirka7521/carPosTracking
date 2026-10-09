using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>PUT /api/me/maps-consent</c> — the "Always load maps" button.
///
/// The version is the dashboard's: the prompt text lives there, so the dashboard is
/// what knows which wording the person was shown. It is stored as sent, which is
/// what makes the record answerable later ("agreed to the 2026-10-09 prompt, at
/// this moment").
/// </summary>
/// <param name="Version">The version of the prompt the person agreed to.</param>
public sealed record MapsConsentGrantRequestDto(
    [Required]
    [StringLength(32, MinimumLength = 1)]
    string Version);
