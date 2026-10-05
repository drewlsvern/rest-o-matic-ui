using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Accounts.Sessions;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Tests.Features.Accounts;

public class SignOutTests
{
    [Fact]
    public async Task Signing_out_ends_the_session_and_the_old_cookie_stops_working()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        var (client, cookies) = await factory.SignedInWithCookiesAsync(alice);
        using var _ = client;
        var oldCookie = cookies.GetCookies(factory.Server.BaseAddress)[AccountSessions.CookieName]!;

        using var response = await client.SendAsync(SignOutRequest(factory, alice, cookies));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SignInEndpoints.SignInPath, response.Headers.Location!.OriginalString);
        Assert.False(await AccountsTestSupport.IsSignedInAsync(client));

        using var replay = factory.CreateClientWithCookies(out var replayCookies);
        replayCookies.Add(factory.Server.BaseAddress, new Cookie(oldCookie.Name, oldCookie.Value));
        Assert.False(await AccountsTestSupport.IsSignedInAsync(replay));
    }

    [Fact]
    public async Task Signing_out_needs_the_antiforgery_token()
    {
        using var factory = new AppFactory();
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        using var response = await client.PostAsync(SignInEndpoints.SignOutPath, new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await AccountsTestSupport.IsSignedInAsync(client));
    }

    [Fact]
    public async Task The_profile_menu_shows_the_display_name()
    {
        using var factory = new AppFactory();
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice", displayName: "Alice Smith"));

        var html = await (await AppFactory.GetPageAsync(client, "/")).Content.ReadAsStringAsync();

        Assert.Matches(@"class=""[^""]*\bmud-appbar\b", html);
        Assert.Contains("Alice Smith", html);
        Assert.Contains(">AS<", html.Replace(" ", "").Replace("\n", ""));
    }

    [Fact]
    public async Task The_profile_menu_shows_the_user_name_without_a_display_name()
    {
        using var factory = new AppFactory();
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("bob"));

        var html = await (await AppFactory.GetPageAsync(client, "/")).Content.ReadAsStringAsync();

        Assert.Matches(@"data-testid=""profile-menu-name""[^>]*>bob<", html);
    }

    /// <summary>
    /// The sign-out form as the profile menu posts it, with an antiforgery
    /// token made for the signed-in user.
    /// </summary>
    private static HttpRequestMessage SignOutRequest(AppFactory factory, User user, CookieContainer cookies)
    {
        var context = new DefaultHttpContext { RequestServices = factory.Services };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], AccountClaims.AuthenticationType));
        var antiforgery = factory.Services.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        var cookieName = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name!;
        cookies.Add(factory.Server.BaseAddress, new Cookie(cookieName, tokens.CookieToken));

        return new HttpRequestMessage(HttpMethod.Post, SignInEndpoints.SignOutPath)
        {
            Content = new FormUrlEncodedContent([new(tokens.FormFieldName, tokens.RequestToken!)]),
        };
    }
}
