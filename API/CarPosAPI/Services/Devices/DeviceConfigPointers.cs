namespace CarPosAPI.Services.Devices;

/// <summary>
/// The four config-related columns of a device row, projected out on their own.
///
/// A named record rather than an anonymous type (this project does not use <c>var</c>)
/// and rather than loading the whole <c>Device</c>, which would pull the protected
/// private-key blob into memory for no reason.
/// </summary>
/// <param name="DesiredVersion">The revision published to the device's config topic.</param>
/// <param name="AppliedVersion">The revision the device last confirmed, or null.</param>
/// <param name="AppliedAt">When that confirmation arrived (UTC), or null.</param>
/// <param name="LastSeenAt">When the device last delivered anything (UTC), or null.</param>
internal sealed record DeviceConfigPointers(
    int DesiredVersion,
    int? AppliedVersion,
    DateTime? AppliedAt,
    DateTime? LastSeenAt);
