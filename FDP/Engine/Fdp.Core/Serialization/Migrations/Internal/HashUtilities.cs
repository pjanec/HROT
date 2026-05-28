using System;
using System.Security.Cryptography;
using System.Text;

namespace Fdp.Core.Serialization.Migrations.Internal;

internal static class HashUtilities
{
    /// <summary>
    /// Computes SHA-256 of <paramref name="content"/> (UTF-8 encoded)
    /// and returns the first 16 hex characters in lowercase.
    /// </summary>
    public static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
