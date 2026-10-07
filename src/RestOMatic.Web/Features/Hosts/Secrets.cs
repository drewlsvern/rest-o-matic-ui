using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace RestOMatic.Web.Features.Hosts;

/// <summary>
/// The two secrets a host is given. Both are long and random, so they are
/// stored as SHA-256 hashes and looked up by hash: a slow password hash is
/// for secrets people choose.
/// </summary>
public static class Secrets
{
    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public const string CredentialPrefix = "rom1_";

    /// <summary>26 characters of base32 (130 bits): short enough to paste, and typed in any case.</summary>
    public static string NewEnrolToken() => new(RandomNumberGenerator.GetItems<char>(Base32, 26));

    /// <summary><c>rom1_</c> and 32 random bytes; the prefix makes a leaked credential recognisable.</summary>
    public static string NewCredential() =>
        CredentialPrefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>An enrol token is compared without regard to case or surrounding spaces.</summary>
    public static byte[] HashEnrolToken(string token) => Hash(token.Trim().ToUpperInvariant());

    public static byte[] HashCredential(string credential) => Hash(credential);

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

/// <summary>What makes a host name acceptable. Unique without regard to letter case.</summary>
public static partial class HostNames
{
    public const int MaxLength = 64;

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    public static IReadOnlyList<string> Check(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ["Enter a name for the host."];
        }
        List<string> problems = [];
        if (name.Trim().Length > MaxLength)
        {
            problems.Add($"A host name can be at most {MaxLength} characters.");
        }
        if (!Characters().IsMatch(name.Trim()))
        {
            problems.Add("A host name can only contain letters, digits, '.', '_' and '-'.");
        }
        return problems;
    }

    [GeneratedRegex(@"^[\p{L}\p{Nd}._-]+$")]
    private static partial Regex Characters();
}
