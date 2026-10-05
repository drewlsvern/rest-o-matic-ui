using Microsoft.AspNetCore.Identity;
using RestOMatic.Web.Features.Accounts;

namespace RestOMatic.Web.Tests.Features.Accounts;

public class AccountRulesTests
{
    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public void Password_length(int length, bool acceptable)
    {
        var password = "Aa1-" + new string('x', length - 4);

        var problems = AccountRules.CheckPassword(password, "alice");

        Assert.Equal(acceptable, problems.Count == 0);
    }

    [Fact]
    public void Too_short_names_the_minimum()
    {
        Assert.Contains("at least 12 characters", Assert.Single(AccountRules.CheckPassword("Aa1-xxxxxxx", "alice")));
    }

    [Fact]
    public void Too_long_names_the_maximum()
    {
        Assert.Contains("at most 255 characters", Assert.Single(AccountRules.CheckPassword("Aa1-" + new string('x', 252), "alice")));
    }

    [Theory]
    [InlineData("correct-horse7", "an upper-case letter")]
    [InlineData("CORRECT-HORSE7", "a lower-case letter")]
    [InlineData("Correct-horse", "a digit")]
    [InlineData("Correcthorse7", "a special character")]
    public void Each_kind_of_character_is_required(string password, string missing)
    {
        var problem = Assert.Single(AccountRules.CheckPassword(password, "alice"));

        Assert.Equal($"A password must contain {missing}.", problem);
    }

    [Fact]
    public void Every_missing_kind_is_named()
    {
        var problem = Assert.Single(AccountRules.CheckPassword("aaaaaaaaaaaa", "alice"));

        Assert.Equal("A password must contain an upper-case letter, a digit and a special character.", problem);
    }

    [Fact]
    public void Every_broken_rule_is_reported_not_just_the_first()
    {
        var problems = AccountRules.CheckPassword("aaa", "alice");

        Assert.Equal(2, problems.Count);
    }

    [Theory]
    [InlineData("Ünïcödé-pass7")]
    [InlineData("ÅSTRÖM-pässwd9")]
    public void Letters_outside_ascii_count_as_upper_or_lower_case(string password)
    {
        Assert.Empty(AccountRules.CheckPassword(password, "alice"));
    }

    [Fact]
    public void A_space_is_a_special_character()
    {
        Assert.Empty(AccountRules.CheckPassword("Correct horse7", "alice"));
    }

    [Fact]
    public void Password_cannot_be_the_user_name()
    {
        Assert.Contains(
            "A password cannot be the same as the user name.",
            AccountRules.CheckPassword("Alice.Smith-9", "alice.smith-9"));
    }

    [Fact]
    public void Example_from_the_spec_is_acceptable()
    {
        Assert.Empty(AccountRules.CheckPassword("Correct-horse7", "alice"));
    }

    [Fact]
    public void Confirmation_must_match()
    {
        Assert.Equal(["The passwords do not match."], AccountRules.CheckConfirmation("Correct-horse7", "Correct-horse8"));
        Assert.Empty(AccountRules.CheckConfirmation("Correct-horse7", "Correct-horse7"));
    }

    [Theory]
    [InlineData("alice", true)]
    [InlineData("alice.smith_2-x@home", true)]
    [InlineData("élodie", true)]
    [InlineData("", false)]
    [InlineData("alice smith", false)]
    [InlineData("alice/smith", false)]
    [InlineData("alice!", false)]
    public void User_name_characters(string userName, bool acceptable)
    {
        Assert.Equal(acceptable, AccountRules.CheckUserName(userName).Count == 0);
    }

    [Fact]
    public void User_name_length()
    {
        Assert.Empty(AccountRules.CheckUserName(new string('a', 64)));
        Assert.NotEmpty(AccountRules.CheckUserName(new string('a', 65)));
    }

    [Theory]
    [InlineData("bob@example.com", true)]
    [InlineData("a@b", true)]
    [InlineData("", false)]
    [InlineData("bob.example.com", false)]
    [InlineData("@example.com", false)]
    [InlineData("bob@", false)]
    [InlineData("bob@one@two", false)]
    public void Email_uses_the_built_in_validator(string email, bool acceptable)
    {
        Assert.Equal(acceptable, AccountRules.CheckEmail(email).Count == 0);
    }

    [Fact]
    public void Display_name_length()
    {
        Assert.Empty(AccountRules.CheckDisplayName(null));
        Assert.Empty(AccountRules.CheckDisplayName(new string('a', 100)));
        Assert.NotEmpty(AccountRules.CheckDisplayName(new string('a', 101)));
    }

    [Fact]
    public void Generated_passwords_meet_the_rules()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = Passwords.Generate();
            Assert.Equal(20, password.Length);
            Assert.Empty(AccountRules.CheckPassword(password, "alice"));
        }
    }

    [Fact]
    public void A_stored_hash_is_not_the_password_and_verifies_it()
    {
        var passwords = new Passwords();
        var user = new User { UserName = "alice", Email = "alice@example.com" };
        user.Password = new PasswordCredential { UserId = user.Id, Hash = passwords.Hash(user, "Correct-horse7") };

        Assert.DoesNotContain("Correct-horse7", user.Password.Hash);
        Assert.True(passwords.Verify(user, "Correct-horse7", out var rehashed));
        Assert.Null(rehashed);
        Assert.False(passwords.Verify(user, "Correct-horse8", out _));
        Assert.False(passwords.Verify(null, "Correct-horse7", out _));
    }

    [Fact]
    public void An_older_hash_format_is_upgraded()
    {
        var passwords = new Passwords();
        var user = new User { UserName = "alice", Email = "alice@example.com" };
        var old = new PasswordHasher<User>(Microsoft.Extensions.Options.Options.Create(
            new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2 }));
        user.Password = new PasswordCredential { UserId = user.Id, Hash = old.HashPassword(user, "Correct-horse7") };

        Assert.True(passwords.Verify(user, "Correct-horse7", out var rehashed));
        Assert.NotNull(rehashed);
    }
}
