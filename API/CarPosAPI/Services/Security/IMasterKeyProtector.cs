namespace CarPosAPI.Services.Security;

/// <summary>
/// Encrypts small secrets (device RSA private key PEMs) for storage at rest in
/// the database, and decrypts them again. The associated data binds a blob to
/// its owning row so ciphertexts cannot be swapped between devices.
/// </summary>
public interface IMasterKeyProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> under the master key.</summary>
    /// <param name="plaintext">The secret to protect (e.g. a PEM string).</param>
    /// <param name="associatedData">
    /// Context bound into the authentication tag — for device keys, the device id.
    /// Decryption with different associated data fails.
    /// </param>
    /// <returns>A self-describing versioned blob safe to store in a bytea column.</returns>
    byte[] Protect(string plaintext, string associatedData);

    /// <summary>Decrypts a blob produced by <see cref="Protect"/>.</summary>
    /// <param name="blob">The stored blob.</param>
    /// <param name="associatedData">Must match the value used when protecting.</param>
    /// <returns>The original plaintext.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Wrong master key, tampered blob, or mismatched associated data.
    /// </exception>
    string Unprotect(byte[] blob, string associatedData);
}
