using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace RestOMatic.Web.Features.Accounts;

/// <summary>
/// Hashing and checking passwords with ASP.NET Core Identity's hasher
/// (PBKDF2 with HMAC-SHA512, versioned format). Full Identity is not used.
/// </summary>
public sealed class Passwords
{
    private const string LowerCase = "abcdefghijkmnopqrstuvwxyz";
    private const string UpperCase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Specials = "-_.!#%+=";

    private readonly PasswordHasher<User> _hasher = new();

    // Checked against when the user name does not exist, so a missing user
    // takes as long to refuse as a wrong password.
    private readonly string _dummyHash;

    public Passwords()
    {
        _dummyHash = _hasher.HashPassword(null!, Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));
    }

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    /// <summary>
    /// Whether <paramref name="password"/> is the user's password. When the
    /// stored hash is in an older format, <paramref name="rehashed"/> is the
    /// same password hashed in the current one, to be saved.
    /// </summary>
    public bool Verify(User? user, string password, out string? rehashed)
    {
        rehashed = null;
        if (user?.Password is null)
        {
            _hasher.VerifyHashedPassword(null!, _dummyHash, password);
            return false;
        }

        switch (_hasher.VerifyHashedPassword(user, user.Password.Hash, password))
        {
            case PasswordVerificationResult.Success:
                return true;
            case PasswordVerificationResult.SuccessRehashNeeded:
                rehashed = Hash(user, password);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A random 20-character password that meets <see cref="AccountRules"/>.
    /// Characters that are easily confused (l, 1, I, O, 0) are left out,
    /// because it is read off a terminal and typed.
    /// </summary>
    public static string Generate()
    {
        const int length = 20;
        var all = LowerCase + UpperCase + Digits + Specials;
        char[] password =
        [
            RandomNumberGenerator.GetItems<char>(LowerCase, 1)[0],
            RandomNumberGenerator.GetItems<char>(UpperCase, 1)[0],
            RandomNumberGenerator.GetItems<char>(Digits, 1)[0],
            RandomNumberGenerator.GetItems<char>(Specials, 1)[0],
            .. RandomNumberGenerator.GetItems<char>(all, length - 4),
        ];
        RandomNumberGenerator.Shuffle(password.AsSpan());
        return new string(password);
    }
}
