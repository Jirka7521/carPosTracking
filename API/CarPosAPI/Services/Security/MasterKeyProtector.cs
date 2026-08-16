using System.Security.Cryptography;
using System.Text;
using CarPosAPI.Options;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Security;

/// <summary>
/// AES-256-GCM implementation of <see cref="IMasterKeyProtector"/> using the
/// 32-byte master key from <see cref="DeviceKeyProtectionOptions"/>.
///
/// Blob layout: <c>[0x01 version][12-byte nonce][ciphertext][16-byte tag]</c>.
/// The leading version byte is the key-rotation hook: a future format (new key,
/// new algorithm) bumps it and readers can dispatch on it. The device id goes in
/// as GCM associated data so a blob copied onto another device's row fails
/// authentication instead of decrypting for the wrong device.
///
/// Registered as a singleton; a fresh <see cref="AesGcm"/> is created per call
/// because the type is not documented thread-safe and the cost is negligible at
/// provisioning/cache-load frequency.
/// </summary>
public sealed class MasterKeyProtector : IMasterKeyProtector
{
    /// <summary>Current blob format version.</summary>
    private const byte FormatVersion = 0x01;

    /// <summary>GCM nonce size in bytes (the standard 96 bits).</summary>
    private const int NonceBytes = 12;

    /// <summary>GCM authentication tag size in bytes (full 128 bits).</summary>
    private const int TagBytes = 16;

    private readonly byte[] _masterKey;

    /// <summary>Decodes and pins the master key once at construction.</summary>
    /// <param name="options">Validated key-protection options.</param>
    public MasterKeyProtector(IOptions<DeviceKeyProtectionOptions> options)
    {
        // Options were validated at startup; decode defensively anyway so a
        // programming error surfaces here and not as a garbage decrypt later.
        byte[] key = Convert.FromBase64String(options.Value.MasterKeyBase64);
        if (key.Length != DeviceKeyProtectionOptions.MasterKeyBytes)
        {
            throw new InvalidOperationException(
                $"Master key must be exactly {DeviceKeyProtectionOptions.MasterKeyBytes} bytes.");
        }

        _masterKey = key;
    }

    /// <inheritdoc />
    public byte[] Protect(string plaintext, string associatedData)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        ArgumentException.ThrowIfNullOrEmpty(associatedData);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] aad = Encoding.UTF8.GetBytes(associatedData);
        byte[] blob = new byte[1 + NonceBytes + plaintextBytes.Length + TagBytes];
        blob[0] = FormatVersion;

        Span<byte> nonce = blob.AsSpan(1, NonceBytes);
        Span<byte> ciphertext = blob.AsSpan(1 + NonceBytes, plaintextBytes.Length);
        Span<byte> tag = blob.AsSpan(1 + NonceBytes + plaintextBytes.Length, TagBytes);

        RandomNumberGenerator.Fill(nonce);
        try
        {
            using AesGcm aes = new AesGcm(_masterKey, TagBytes);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
        }
        finally
        {
            // The caller keeps the plaintext string (strings can't be zeroed), but
            // at least this transient copy of the secret is wiped.
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }

        return blob;
    }

    /// <inheritdoc />
    public string Unprotect(byte[] blob, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentException.ThrowIfNullOrEmpty(associatedData);

        // Version + nonce + tag + at least one ciphertext byte.
        if (blob.Length < 1 + NonceBytes + TagBytes + 1)
        {
            throw new CryptographicException("Protected blob is truncated.");
        }

        if (blob[0] != FormatVersion)
        {
            throw new CryptographicException($"Unknown protected-blob version {blob[0]}.");
        }

        byte[] aad = Encoding.UTF8.GetBytes(associatedData);
        ReadOnlySpan<byte> nonce = blob.AsSpan(1, NonceBytes);
        ReadOnlySpan<byte> ciphertext = blob.AsSpan(1 + NonceBytes, blob.Length - 1 - NonceBytes - TagBytes);
        ReadOnlySpan<byte> tag = blob.AsSpan(blob.Length - TagBytes, TagBytes);

        byte[] plaintextBytes = new byte[ciphertext.Length];
        try
        {
            using AesGcm aes = new AesGcm(_masterKey, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes, aad);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }
}
