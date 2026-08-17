namespace CarPosAPI.Dtos;

/// <summary>
/// What the dashboard needs to render the settings panel: what the device
/// <em>should</em> be running, what it <em>is</em> running, and whether those agree.
///
/// <para>
/// Both halves carry full values, not just version numbers. That is the point of
/// keeping every revision: while a change is pending the UI can show "reporting every
/// 60 s, will become every 300 s" instead of the useless "running v5, published v7".
/// </para>
/// </summary>
/// <param name="Desired">The revision currently published to the device's config topic.</param>
/// <param name="Applied">
/// The revision the device last confirmed. Null when it has never reported one, which
/// is not an error — a device that has not yet checked in, or one running firmware
/// older than the settings-version protocol, both look like this.
/// </param>
/// <param name="AppliedAt">When <paramref name="Applied"/> was confirmed (UTC), or null.</param>
/// <param name="IsInSync">
/// True when the device has confirmed the desired revision. False means a change is
/// waiting to be picked up — normal, and the UI says so rather than warning.
/// </param>
/// <param name="LastSeenAt">
/// When the device last delivered anything (UTC). Lets the dashboard tell "picked the
/// change up already" apart from "has not been heard from since it was published".
/// </param>
public sealed record DeviceConfigStateDto(
    DeviceConfigVersionDto Desired,
    DeviceConfigVersionDto? Applied,
    DateTime? AppliedAt,
    bool IsInSync,
    DateTime? LastSeenAt);
