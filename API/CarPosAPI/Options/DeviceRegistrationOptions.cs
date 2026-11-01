using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Options;

/// <summary>
/// Limits on registering devices, bound from the <c>DeviceRegistration</c>
/// section. Nothing here is secret — like <see cref="SharingOptions"/>, these are
/// policy ceilings kept in configuration so a deployment can change them without a
/// rebuild.
/// </summary>
public sealed class DeviceRegistrationOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "DeviceRegistration";

    /// <summary>
    /// How many active devices one account may have registered at once.
    ///
    /// <para>
    /// Registration is the most expensive thing an account can ask for: every call
    /// generates an RSA-3072 key pair on a Raspberry Pi and writes rows that are
    /// never physically deleted. Any signed-in account can call it, and sign-up is
    /// open, so without a ceiling one account is a lever on the whole server. The
    /// per-account rate limit (<c>RateLimitPolicies.DeviceRegistration</c>) bounds how
    /// fast; this bounds how many.
    /// </para>
    ///
    /// <para>
    /// Counted over devices the account registered itself and has not deleted —
    /// devices shared <em>with</em> it are someone else's. Twenty is far beyond a
    /// personal fleet.
    /// </para>
    /// </summary>
    [Range(1, 1000)]
    public int MaxActiveDevicesPerUser { get; set; } = 20;
}
