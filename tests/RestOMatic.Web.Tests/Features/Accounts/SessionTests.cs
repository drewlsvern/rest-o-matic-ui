using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Accounts.Sessions;
using RestOMatic.Web.Features.Accounts.SignIn;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Tests.Features.Accounts;

public class SessionTests
{
    [Fact]
    public async Task A_session_ends_after_12_hours_without_a_request()
    {
        var clock = AccountsTestSupport.NewClock();
        using var factory = AccountsTestSupport.Start(clock);
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        clock.Advance(TimeSpan.FromHours(11));
        Assert.True(await AccountsTestSupport.IsSignedInAsync(client));

        clock.Advance(TimeSpan.FromHours(12) + TimeSpan.FromMinutes(1));
        Assert.False(await AccountsTestSupport.IsSignedInAsync(client));
    }

    [Fact]
    public async Task A_session_in_constant_use_ends_after_7_days()
    {
        var clock = AccountsTestSupport.NewClock();
        using var factory = AccountsTestSupport.Start(clock);
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        var elapsed = TimeSpan.Zero;
        while (elapsed + TimeSpan.FromHours(11) <= TimeSpan.FromDays(7))
        {
            clock.Advance(TimeSpan.FromHours(11));
            elapsed += TimeSpan.FromHours(11);
            Assert.True(await AccountsTestSupport.IsSignedInAsync(client), $"Still signed in after {elapsed}");
        }

        clock.Advance(TimeSpan.FromHours(11));
        Assert.False(await AccountsTestSupport.IsSignedInAsync(client));
    }

    [Fact]
    public async Task A_restart_ends_every_session()
    {
        var dataDirectory = AppFactory.NewTemporaryPath();
        try
        {
            Cookie sessionCookie;
            using (var first = new AppFactory(dataDirectory))
            {
                var (client, cookies) = await first.SignedInWithCookiesAsync(await first.CreateUserAsync("alice"));
                using (client)
                {
                    Assert.True(await AccountsTestSupport.IsSignedInAsync(client));
                }
                sessionCookie = cookies.GetCookies(first.Server.BaseAddress)[AccountSessions.CookieName]!;
            }

            using var second = new AppFactory(dataDirectory);
            using var again = second.CreateClientWithCookies(out var secondCookies);
            secondCookies.Add(second.Server.BaseAddress, new Cookie(sessionCookie.Name, sessionCookie.Value));

            // The same data-protection keys still read the cookie; its session is gone.
            Assert.NotNull(AccountsTestSupport.ReadSessionCookie(second, secondCookies));
            Assert.False(await AccountsTestSupport.IsSignedInAsync(again));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task A_changed_security_stamp_ends_the_users_sessions()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        using var client = await factory.CreateSignedInClientAsync(alice);

        // As the reset command does from another process: the stamp changes
        // in the database and the in-memory session is left alone.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Set<User>().SingleAsync(u => u.Id == alice.Id);
            user.ChangeSecurityStamp();
            await db.SaveChangesAsync();
        }

        Assert.False(await AccountsTestSupport.IsSignedInAsync(client));
    }

    [Fact]
    public async Task The_cookie_holds_only_a_session_key()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice", displayName: "Alice Smith");
        var (client, cookies) = await factory.SignedInWithCookiesAsync(alice);
        client.Dispose();

        var raw = cookies.GetCookies(factory.Server.BaseAddress)[AccountSessions.CookieName]!.Value;
        var ticket = AccountsTestSupport.ReadSessionCookie(factory, cookies)!;

        Assert.DoesNotContain("alice", raw, StringComparison.OrdinalIgnoreCase);
        var claim = Assert.Single(ticket.Principal.Claims);
        Assert.Equal("Microsoft.AspNetCore.Authentication.Cookies-SessionId", claim.Type);
        Assert.DoesNotContain(alice.Id.ToString(), claim.Value);
    }

    [Fact]
    public async Task The_cookie_is_http_only_and_strict_and_secure_outside_development()
    {
        using var factory = AccountsTestSupport.Start(environment: "Production");
        var alice = await factory.CreateUserAsync("alice");
        using var client = factory.CreateClientWithCookies(out var cookies);
        cookies.Add(factory.Server.BaseAddress, new Cookie(BrowserBinding.CookieName, "b"));
        var ticket = factory.Services.GetRequiredService<SignInTickets>().Issue(alice.Id, "/", "b");

        using var response = await client.GetAsync(SignInHandler.CompletionUrl(ticket));

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(AccountSessions.CookieName + "="));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_open_page_becomes_signed_out_as_soon_as_its_session_ends()
    {
        var store = new SessionStore(TimeProvider.System);
        using var factory = new AppFactory();
        using var provider = new SessionAuthenticationStateProvider(
            NullLoggerFactory.Instance, factory.Services.GetRequiredService<IServiceScopeFactory>(), store);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(AccountClaims.Session, "session-1")],
            AccountClaims.AuthenticationType));
        await store.StoreAsync(new AuthenticationTicket(principal, "Cookies"));
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));
        var changed = new TaskCompletionSource<AuthenticationState>();
        provider.AuthenticationStateChanged += async task => changed.TrySetResult(await task);

        await store.RemoveAsync("session-1");

        var state = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task Another_sessions_end_does_not_affect_an_open_page()
    {
        var store = new SessionStore(TimeProvider.System);
        using var factory = new AppFactory();
        using var provider = new SessionAuthenticationStateProvider(
            NullLoggerFactory.Instance, factory.Services.GetRequiredService<IServiceScopeFactory>(), store);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AccountClaims.Session, "session-1")], AccountClaims.AuthenticationType));
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));

        await store.RemoveAsync("session-2");

        Assert.True((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task Signed_out_requests_for_pages_are_redirected_with_the_page_to_return_to()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateDirectClient();

        using var response = await AppFactory.GetPageAsync(client, "/account/users?sort=name");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/sign-in?returnUrl=%2Faccount%2Fusers%3Fsort%3Dname", response.Headers.Location!.PathAndQuery);
    }
}
