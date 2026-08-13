using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Options;

/// <summary>
/// Master-key configuration for encrypting device RSA private keys at rest in the
/// database (see <see cref="Services.Security.MasterKeyProtector"/>). The key is a
/// secret: user-secrets in development, an environment variable in production.
/// Startup fails if it is missing or malformed — running without it would either
/// crash on first message or tempt someone to store keys in plaintext.
/// </summary>
public sealed class DeviceKeyProtectionOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "DeviceKeyProtection";

    /// <summary>Required master key length in bytes — AES-256 needs exactly 32.</summary>
    public const int MasterKeyBytes = 32;

    /// <summary>Base64 of exactly 32 random bytes. Secret — never in tracked files.</summary>
    [Required]
    public string MasterKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Validates that <see cref="MasterKeyBase64"/> decodes to exactly 32 bytes.
    /// Wired into the options pipeline in <c>Program.cs</c> so a truncated or
    /// mistyped key aborts startup instead of failing on the first decrypt.
    /// </summary>
    /// <returns><c>true</c> when the value is valid base64 of 32 bytes.</returns>
    public bool HasValidMasterKey()
    {
        try
        {
            byte[] decoded = Convert.FromBase64String(MasterKeyBase64);
            return decoded.Length == MasterKeyBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
