namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// The verdict on a candidate ack public key: either a fingerprint to store it under,
/// or the reason it was refused. Never both.
/// </summary>
/// <param name="Error">
/// Why the key is unusable, phrased for whoever supplied it — a dashboard user or the
/// operator running the CLI — or null when it is acceptable.
/// </param>
/// <param name="Fingerprint">
/// SHA-256 over the DER SubjectPublicKeyInfo, uppercase hex; null when
/// <paramref name="Error"/> is set.
/// </param>
internal sealed record AckPublicKeyValidation(string? Error, string? Fingerprint)
{
    /// <summary>True when the key may be stored.</summary>
    public bool IsValid => Error is null;
}
