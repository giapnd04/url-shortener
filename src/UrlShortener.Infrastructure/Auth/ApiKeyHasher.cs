using System.Security.Cryptography;
using System.Text;

namespace UrlShortener.Infrastructure.Auth;

/// <summary>
/// Plain SHA-256 is enough for API keys: a 32-byte random key has so much entropy that
/// brute force is hopeless even with a fast hash, and a fast deterministic hash lets us
/// find the client with a unique-index seek. Passwords are low-entropy and need a slow,
/// salted hash (PBKDF2/bcrypt/Argon2) instead.
/// </summary>
public static class ApiKeyHasher
{
    public static byte[] Hash(string apiKey) => SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

    /// <summary>Creates a new random key (shown to its owner once, never stored).</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
