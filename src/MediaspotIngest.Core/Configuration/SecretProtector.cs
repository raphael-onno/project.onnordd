using System.Security.Cryptography;
using System.Text;

namespace MediaspotIngest.Core.Configuration;

/// <summary>
/// Encrypts secrets for the current Windows user (DPAPI), so the settings file never holds a password in clear —
/// the old tool had every credential compiled into the executable.
/// </summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = "MediaspotIngest.v1"u8.ToArray();

    public static string? Protect(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return null;

        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    public static string? Unprotect(string? protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret))
            return null;

        try
        {
            var clear = ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Settings copied from another user or machine: the secret has to be typed again.
            return null;
        }
    }
}
