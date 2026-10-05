using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RestOMatic.Web.Features.Accounts.Sessions;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Tests.Features.Accounts;

public class SignInTests
{
    [Fact]
    public async Task The_right_password_signs_in_and_returns_to_the_page_asked_for()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateClientWithCookies(out var cookies);
        cookies.Add(factory.Server.BaseAddress, new Cookie(BrowserBinding.CookieName, "binding"));

        using var scope = factory.Services.CreateScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<SignInHandler>()
            .SignInAsync("alice", AppFactory.Password, "10.0.0.1", "binding", "/account/users");
        var succeeded = Assert.IsType<SignInOutcome.Succeeded>(outcome);

        using var response = await client.GetAsync(succeeded.CompletionUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/users", response.Headers.Location!.OriginalString);
        Assert.NotNull(cookies.GetCookies(factory.Server.BaseAddress)[AccountSessions.CookieName]);
        Assert.True(await AccountsTestSupport.IsSignedInAsync(client));
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_user_get_the_same_message()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");

        var wrong = Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice", "Wrong-horse7"));
        var unknown = Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("nobody", "Wrong-horse7"));

        Assert.Equal("Incorrect user name or password.", wrong.Message);
        Assert.Equal(wrong.Message, unknown.Message);
    }

    [Fact]
    public async Task User_names_ignore_letter_case()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");

        var outcome = Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("Alice", AppFactory.Password));

        using var client = factory.CreateClientWithCookies(out var cookies);
        cookies.Add(factory.Server.BaseAddress, new Cookie(BrowserBinding.CookieName, "binding"));
        await client.GetAsync(outcome.CompletionUrl);
        var ticket = await factory.Services.GetRequiredService<SessionStore>()
            .RetrieveAsync(AccountsTestSupport.SessionIdOf(factory, cookies));
        var principal = ticket!.Principal;
        Assert.Equal(alice.Id, AccountClaims.UserId(principal));
    }

    [Fact]
    public async Task An_email_address_is_not_a_user_name()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice", email: "alice@example.com");

        Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice@example.com", AppFactory.Password));
    }

    [Fact]
    public async Task Five_failures_lock_the_user_name_with_a_growing_wait()
    {
        var clock = AccountsTestSupport.NewClock();
        using var factory = AccountsTestSupport.Start(clock);
        await factory.CreateUserAsync("alice");

        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal("Incorrect user name or password.",
                Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice", "Wrong-horse7", client: $"10.0.1.{i}")).Message);
        }
        var fifth = Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice", "Wrong-horse7", client: "10.0.1.5"));
        Assert.Contains("Try again in 30 seconds", fifth.Message);

        var locked = Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice", AppFactory.Password, client: "10.0.1.6"));
        Assert.Equal("Too many failed attempts for this user. Try again in 30 seconds.", locked.Message);

        clock.Advance(TimeSpan.FromSeconds(31));
        var sixth = Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice", "Wrong-horse7", client: "10.0.1.7"));
        Assert.Contains("Try again in 60 seconds", sixth.Message);

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("alice", AppFactory.Password, client: "10.0.1.8"));
    }

    [Fact]
    public async Task The_lock_never_exceeds_15_minutes()
    {
        var clock = AccountsTestSupport.NewClock();
        using var factory = AccountsTestSupport.Start(clock);
        await factory.CreateUserAsync("alice");

        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(16));
            await factory.SignInAsync("alice", "Wrong-horse7", client: $"10.0.2.{i}");
        }

        var locked = Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice", AppFactory.Password, client: "10.0.3.1"));
        Assert.EndsWith("Try again in 15 minutes.", locked.Message);
    }

    [Fact]
    public async Task A_success_resets_the_count()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");

        for (var i = 0; i < 4; i++)
        {
            await factory.SignInAsync("alice", "Wrong-horse7", client: $"10.0.4.{i}");
        }
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("alice", AppFactory.Password, client: "10.0.4.9"));
        for (var i = 0; i < 4; i++)
        {
            await factory.SignInAsync("alice", "Wrong-horse7", client: $"10.0.5.{i}");
        }

        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("alice", AppFactory.Password, client: "10.0.5.9"));
    }

    [Fact]
    public async Task One_client_is_limited_whatever_names_it_tries()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");

        for (var i = 0; i < SignInThrottle.AttemptsPerMinute; i++)
        {
            Assert.Equal(SignInHandler.WrongCredentials,
                Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync($"user{i}", "Wrong-horse7", client: "10.0.6.1")).Message);
        }

        var refused = Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice", AppFactory.Password, client: "10.0.6.1"));
        Assert.Equal(SignInHandler.TooManyFromClient, refused.Message);
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("alice", AppFactory.Password, client: "10.0.6.2"));
    }

    [Fact]
    public async Task A_ticket_works_only_once()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        var ticket = factory.Services.GetRequiredService<SignInTickets>().Issue(alice.Id, "/", "binding");

        Assert.True(await RedeemAsync(factory, ticket, "binding"));
        Assert.False(await RedeemAsync(factory, ticket, "binding"));
    }

    [Fact]
    public async Task A_ticket_expires_after_30_seconds()
    {
        var clock = AccountsTestSupport.NewClock();
        using var factory = AccountsTestSupport.Start(clock);
        var alice = await factory.CreateUserAsync("alice");
        var ticket = factory.Services.GetRequiredService<SignInTickets>().Issue(alice.Id, "/", "binding");

        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.False(await RedeemAsync(factory, ticket, "binding"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("another-browser")]
    public async Task A_ticket_works_only_in_the_browser_it_was_made_for(string? binding)
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        var ticket = factory.Services.GetRequiredService<SignInTickets>().Issue(alice.Id, "/", "binding");

        Assert.False(await RedeemAsync(factory, ticket, binding));
    }

    [Theory]
    [InlineData("https://elsewhere.example/")]
    [InlineData("//elsewhere.example/")]
    [InlineData("/\\elsewhere.example/")]
    [InlineData("javascript:alert(1)")]
    public async Task A_return_address_on_another_site_goes_to_the_home_page(string returnUrl)
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateClientWithCookies(out var cookies);
        cookies.Add(factory.Server.BaseAddress, new Cookie(BrowserBinding.CookieName, "binding"));
        using var scope = factory.Services.CreateScope();

        var outcome = Assert.IsType<SignInOutcome.Succeeded>(await scope.ServiceProvider.GetRequiredService<SignInHandler>()
            .SignInAsync("alice", AppFactory.Password, "10.0.0.1", "binding", returnUrl));
        using var response = await client.GetAsync(outcome.CompletionUrl);

        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_sign_in_page_is_centred_with_the_logo_and_no_app_bar()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateDirectClient();

        using var response = await AppFactory.GetPageAsync(client, SignInEndpoints.SignInPath);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"centred-layout\"", html);
        Assert.Contains("data-testid=\"logo\"", html);
        Assert.Contains("images/rest-o-matic.svg", html);
        Assert.DoesNotMatch(@"class=""[^""]*\bmud-appbar\b", html);
        Assert.Contains(BrowserBinding.CookieName, string.Join(";", response.Headers.GetValues("Set-Cookie")));
    }

    private static async Task<bool> RedeemAsync(AppFactory factory, string ticket, string? binding)
    {
        using var client = factory.CreateClientWithCookies(out var cookies);
        if (binding is not null)
        {
            cookies.Add(factory.Server.BaseAddress, new Cookie(BrowserBinding.CookieName, binding));
        }
        using var response = await client.GetAsync(SignInHandler.CompletionUrl(ticket));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return cookies.GetCookies(factory.Server.BaseAddress)[AccountSessions.CookieName] is not null;
    }
}
