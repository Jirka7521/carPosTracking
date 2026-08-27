namespace CarPosAPI.Dtos;

/// <summary>
/// A named settings preset as the dashboard sees it.
///
/// <para>
/// The values are a <see cref="DeviceConfigValuesDto"/> — the same shape a revision
/// carries — so the profile editor and the manual settings form can share every
/// control, label and unit helper the dashboard already has, and so the panel can diff
/// a profile against what the device is running with the code that already does that.
/// </para>
/// </summary>
/// <param name="Id">Stable identifier; rules and the fallback reference it.</param>
/// <param name="Name">What a person calls it. Unique per device, case-insensitively.</param>
/// <param name="Values">The seven settings applied whenever this profile is in force.</param>
/// <param name="CreatedAt">When the profile was created (UTC).</param>
/// <param name="UpdatedAt">When its values were last edited (UTC).</param>
/// <param name="CreatedBy">Display name of whoever created it, or null when that account is gone.</param>
public sealed record DeviceConfigProfileDto(
    Guid Id,
    string Name,
    DeviceConfigValuesDto Values,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? CreatedBy);
