using System.Net;
using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Tests.Features.Accounts;

public class UserAccountsTests
{
    [Fact]
    public async Task An_added_user_can_sign_in()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");

        var (result, _) = await factory.WithAccountsAsync(accounts =>
            accounts.CreateAsync(new NewUser("bob", "bob@example.com", null, AppFactory.Password, AppFactory.Password)));

        Assert.True(result.Succeeded);
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("bob", AppFactory.Password));
    }

    [Fact]
    public async Task Every_problem_with_a_new_user_is_reported()
    {
        using var factory = new AppFactory();

        var (result, user) = await factory.WithAccountsAsync(accounts =>
            accounts.CreateAsync(new NewUser("bob smith", "bob.example.com", null, "short", "different")));

        Assert.Null(user);
        Assert.Equal(
            [
                "A user name can only contain letters, digits, '.', '_', '-' and '@'.",
                "Enter a valid email address.",
                "A password must be at least 12 characters.",
                "A password must contain an upper-case letter, a digit and a special character.",
                "The passwords do not match.",
            ],
            result.Problems);
    }

    [Fact]
    public async Task Names_and_emails_are_unique_whatever_the_letter_case()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice", email: "alice@example.com");

        var (name, _) = await factory.WithAccountsAsync(accounts =>
            accounts.CreateAsync(new NewUser("Alice", "other@example.com", null, AppFactory.Password, AppFactory.Password)));
        var (email, _) = await factory.WithAccountsAsync(accounts =>
            accounts.CreateAsync(new NewUser("bob", "ALICE@Example.com", null, AppFactory.Password, AppFactory.Password)));

        Assert.Equal(["That user name is already taken."], name.Problems);
        Assert.Equal(["That email address is already in use."], email.Problems);
    }

    [Fact]
    public async Task Editing_a_profile_keeps_every_session()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        using var client = await factory.CreateSignedInClientAsync(alice);

        var result = await factory.WithAccountsAsync(accounts => accounts.UpdateProfileAsync(alice.Id, "Al", "al@example.com"));

        Assert.True(result.Succeeded);
        Assert.True(await AccountsTestSupport.IsSignedInAsync(client));
        var summary = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        Assert.Equal("Al", summary!.ShownName);
        Assert.Equal("al@example.com", summary.Email);
    }

    [Fact]
    public async Task Clearing_the_display_name_shows_the_user_name()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice", displayName: "Alice Smith");

        await factory.WithAccountsAsync(accounts => accounts.UpdateProfileAsync(alice.Id, "  ", "alice@example.com"));

        var summary = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        Assert.Null(summary!.DisplayName);
        Assert.Equal("alice", summary.ShownName);
    }

    [Fact]
    public async Task Setting_another_users_password_ends_their_sessions()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        var bob = await factory.CreateUserAsync("bob");
        using var bobsClient = await factory.CreateSignedInClientAsync(bob);

        var result = await factory.WithAccountsAsync(accounts => accounts.SetPasswordAsync(bob.Id, "New-password9", "New-password9"));

        Assert.True(result.Succeeded);
        Assert.False(await AccountsTestSupport.IsSignedInAsync(bobsClient));
        Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("bob", AppFactory.Password));
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("bob", "New-password9", client: "10.0.0.2"));
    }

    [Fact]
    public async Task A_password_that_does_not_match_its_confirmation_is_not_saved()
    {
        using var factory = new AppFactory();
        var bob = await factory.CreateUserAsync("bob");

        var result = await factory.WithAccountsAsync(accounts => accounts.SetPasswordAsync(bob.Id, "New-password9", "New-password8"));

        Assert.Equal(["The passwords do not match."], result.Problems);
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("bob", AppFactory.Password));
    }

    [Fact]
    public async Task Removing_a_user_ends_their_sessions_and_sign_in()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        var bob = await factory.CreateUserAsync("bob");
        using var bobsClient = await factory.CreateSignedInClientAsync(bob);

        var result = await factory.WithAccountsAsync(accounts => accounts.RemoveAsync(alice.Id, bob.Id));

        Assert.True(result.Succeeded);
        Assert.False(await AccountsTestSupport.IsSignedInAsync(bobsClient));
        Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("bob", AppFactory.Password));
    }

    [Fact]
    public async Task You_cannot_remove_yourself()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        await factory.CreateUserAsync("bob");

        var result = await factory.WithAccountsAsync(accounts => accounts.RemoveAsync(alice.Id, alice.Id));

        Assert.Equal(["You cannot remove your own account."], result.Problems);
    }

    [Fact]
    public async Task The_last_user_cannot_be_removed()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");

        var result = await factory.WithAccountsAsync(accounts => accounts.RemoveAsync(Guid.NewGuid(), alice.Id));

        Assert.Equal(["The last user cannot be removed."], result.Problems);
    }

    [Fact]
    public async Task Changing_your_own_password_keeps_this_session_and_ends_the_others()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        var (here, hereCookies) = await factory.SignedInWithCookiesAsync(alice);
        var (elsewhere, _) = await factory.SignedInWithCookiesAsync(alice);
        using var _1 = here;
        using var _2 = elsewhere;
        var thisSession = AccountsTestSupport.SessionIdOf(factory, hereCookies);

        var result = await factory.WithAccountsAsync(accounts =>
            accounts.ChangeOwnPasswordAsync(alice.Id, AppFactory.Password, "New-password9", "New-password9", thisSession));

        Assert.True(result.Succeeded);
        Assert.True(await AccountsTestSupport.IsSignedInAsync(here));
        Assert.False(await AccountsTestSupport.IsSignedInAsync(elsewhere));
    }

    [Fact]
    public async Task Changing_your_own_password_needs_the_current_one()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");

        var result = await factory.WithAccountsAsync(accounts =>
            accounts.ChangeOwnPasswordAsync(alice.Id, "Wrong-horse7", "New-password9", "New-password9", null));

        Assert.Equal(["The current password is incorrect."], result.Problems);
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("alice", AppFactory.Password));
    }

    [Fact]
    public async Task The_users_page_lists_everyone()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice", displayName: "Alice Smith");
        await factory.CreateUserAsync("bob", email: "bob@example.org");
        using var client = await factory.CreateSignedInClientAsync(alice);

        using var response = await AppFactory.GetPageAsync(client, "/account/users");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alice Smith", html);
        Assert.Contains("bob@example.org", html);
        // Remove is offered for bob, never for the signed-in user.
        Assert.Contains("aria-label=\"Remove bob\"", html);
        Assert.DoesNotContain("aria-label=\"Remove alice\"", html);
    }
}
