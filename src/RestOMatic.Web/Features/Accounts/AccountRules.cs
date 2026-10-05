using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace RestOMatic.Web.Features.Accounts;

/// <summary>
/// What makes a user name, email address, display name and password
/// acceptable. Every form and every handler uses these, so the browser and
/// the server cannot disagree. Each check returns every problem it finds,
/// not just the first.
/// </summary>
public static partial class AccountRules
{
    public const int UserNameMaxLength = 64;
    public const int DisplayNameMaxLength = 100;
    public const int PasswordMinLength = 12;
    public const int PasswordMaxLength = 255;

    private static readonly EmailAddressAttribute EmailAddress = new();

    /// <summary>How names and email addresses are compared: trimmed, without regard to letter case.</summary>
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public static IReadOnlyList<string> CheckUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return ["Enter a user name."];
        }
        List<string> problems = [];
        if (userName.Length > UserNameMaxLength)
        {
            problems.Add($"A user name can be at most {UserNameMaxLength} characters.");
        }
        if (!UserNameCharacters().IsMatch(userName))
        {
            problems.Add("A user name can only contain letters, digits, '.', '_', '-' and '@'.");
        }
        return problems;
    }

    public static IReadOnlyList<string> CheckEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ["Enter an email address."];
        }
        return EmailAddress.IsValid(email) ? [] : ["Enter a valid email address."];
    }

    public static IReadOnlyList<string> CheckDisplayName(string? displayName) =>
        displayName is not null && displayName.Trim().Length > DisplayNameMaxLength
            ? [$"A display name can be at most {DisplayNameMaxLength} characters."]
            : [];

    public static IReadOnlyList<string> CheckPassword(string? password, string? userName)
    {
        if (string.IsNullOrEmpty(password))
        {
            return ["Enter a password."];
        }
        List<string> problems = [];
        if (password.Length < PasswordMinLength)
        {
            problems.Add($"A password must be at least {PasswordMinLength} characters.");
        }
        if (password.Length > PasswordMaxLength)
        {
            problems.Add($"A password can be at most {PasswordMaxLength} characters.");
        }

        List<string> missing = [];
        if (!password.Any(char.IsLower))
        {
            missing.Add("a lower-case letter");
        }
        if (!password.Any(char.IsUpper))
        {
            missing.Add("an upper-case letter");
        }
        if (!password.Any(char.IsDigit))
        {
            missing.Add("a digit");
        }
        if (password.All(char.IsLetterOrDigit))
        {
            missing.Add("a special character");
        }
        if (missing.Count > 0)
        {
            problems.Add($"A password must contain {JoinWithAnd(missing)}.");
        }

        if (!string.IsNullOrWhiteSpace(userName) && Normalize(password) == Normalize(userName))
        {
            problems.Add("A password cannot be the same as the user name.");
        }
        return problems;
    }

    public static IReadOnlyList<string> CheckConfirmation(string? password, string? confirmation) =>
        string.Equals(password, confirmation, StringComparison.Ordinal) ? [] : ["The passwords do not match."];

    /// <summary>A new password with its confirmation, as every form that sets one asks for it.</summary>
    public static IReadOnlyList<string> CheckNewPassword(string? password, string? confirmation, string? userName) =>
        [.. CheckPassword(password, userName), .. CheckConfirmation(password, confirmation)];

    private static string JoinWithAnd(List<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items[..^1])} and {items[^1]}";

    [GeneratedRegex(@"^[\p{L}\p{Nd}._@-]+$")]
    private static partial Regex UserNameCharacters();
}
