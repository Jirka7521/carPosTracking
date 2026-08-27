namespace CarPosAPI.Services.Devices;

/// <summary>
/// What <see cref="IDeviceConfigRevisionWriter"/> did.
///
/// <para>
/// <see cref="Changed"/> is the interesting half, and both callers need it for
/// different reasons: the dashboard uses it to word the confirmation it shows
/// ("saved and published as v7" versus "settings saved"), and the scheduler uses it to
/// decide whether the pass is worth a log line — a tick that reconciles a fleet
/// already in its correct state must be silent, or the log becomes a metronome.
/// </para>
/// </summary>
/// <param name="Version">The revision now in force — the new one, or the existing one.</param>
/// <param name="Changed">False when the values already matched and no revision was appended.</param>
internal sealed record ConfigRevisionOutcome(int Version, bool Changed);
