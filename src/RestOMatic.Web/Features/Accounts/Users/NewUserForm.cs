using System.ComponentModel.DataAnnotations;

namespace RestOMatic.Web.Features.Accounts.Users;

/// <summary>
/// The fields for a new user, checked in the form with the same
/// <see cref="AccountRules"/> the handler applies.
/// </summary>
public class NewUserForm : IValidatableObject
{
    public string UserName { get; set; } = "";

    public string Email { get; set; } = "";

    public string? DisplayName { get; set; }

    public string Password { get; set; } = "";

    public string Confirmation { get; set; } = "";

    public NewUser ToNewUser() => new(UserName, Email, DisplayName, Password, Confirmation);

    public void ClearPasswords()
    {
        Password = "";
        Confirmation = "";
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
    [
        .. Results(AccountRules.CheckUserName(UserName), nameof(UserName)),
        .. Results(AccountRules.CheckEmail(Email), nameof(Email)),
        .. Results(AccountRules.CheckDisplayName(DisplayName), nameof(DisplayName)),
        .. Results(AccountRules.CheckPassword(Password, UserName), nameof(Password)),
        .. Results(AccountRules.CheckConfirmation(Password, Confirmation), nameof(Confirmation)),
    ];

    internal static IEnumerable<ValidationResult> Results(IEnumerable<string> problems, string member) =>
        problems.Select(problem => new ValidationResult(problem, [member]));
}

/// <summary>A new password typed twice, as every form that sets one asks for it.</summary>
public sealed class NewPasswordForm(string userName) : IValidatableObject
{
    public string Password { get; set; } = "";

    public string Confirmation { get; set; } = "";

    public void Clear()
    {
        Password = "";
        Confirmation = "";
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
    [
        .. NewUserForm.Results(AccountRules.CheckPassword(Password, userName), nameof(Password)),
        .. NewUserForm.Results(AccountRules.CheckConfirmation(Password, Confirmation), nameof(Confirmation)),
    ];
}

/// <summary>The parts of a profile anyone may edit: display name and email.</summary>
public sealed class ProfileForm : IValidatableObject
{
    public string Email { get; set; } = "";

    public string? DisplayName { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
    [
        .. NewUserForm.Results(AccountRules.CheckEmail(Email), nameof(Email)),
        .. NewUserForm.Results(AccountRules.CheckDisplayName(DisplayName), nameof(DisplayName)),
    ];
}
