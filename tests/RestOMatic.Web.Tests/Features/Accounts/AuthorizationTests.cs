using System.Net;
using System.Text.RegularExpressions;
using RestOMatic.Web.Features.Health;

namespace RestOMatic.Web.Tests.Features.Accounts;

public partial class AuthorizationTests
{
    [Fact]
    public async Task A_page_redirects_to_sign_in()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateDirectClient();

        using var response = await AppFactory.GetPageAsync(client, "/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/sign-in?returnUrl=%2F", response.Headers.Location!.PathAndQuery);
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("POST", "/")]
    [InlineData("GET", "/account/users/00000000-0000-0000-0000-000000000001/picture")]
    [InlineData("GET", "/no-such-thing")]
    public async Task Anything_that_is_not_a_page_gets_401_not_a_redirect(string method, string url)
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateDirectClient();

        using var response = await client.SendAsync(AccountsTestSupport.NotAPage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Every script, stylesheet and image the sign-in page refers to loads
    /// without a session, in Development and in Production, which serve the
    /// framework's script from different places.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Everything_the_sign_in_page_loads_is_open(string environment)
    {
        using var factory = AccountsTestSupport.Start(environment: environment);
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateDirectClient();
        var html = await client.GetStringAsync("/account/sign-in");

        var assets = AssetReference().Matches(html).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains(assets, asset => asset.Contains("blazor.web", StringComparison.Ordinal));
        Assert.Contains(assets, asset => asset.Contains("rest-o-matic.svg", StringComparison.Ordinal));
        foreach (var asset in assets)
        {
            using var response = await client.GetAsync("/" + asset.TrimStart('/'));
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{asset}: {response.StatusCode}");
        }
    }

    [GeneratedRegex("""(?:src|href)="([^"#]+\.(?:js|css|svg|png|ico))" """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex AssetReference();

    [Fact]
    public async Task Health_is_open()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateDirectClient();

        using var response = await client.GetAsync(HealthFeature.Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_sign_in_page_can_open_its_circuit_without_a_cookie()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateDirectClient();

        using var response = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("connectionToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_signed_in_user_reaches_pages()
    {
        using var factory = new AppFactory();
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        using var response = await AppFactory.GetPageAsync(client, "/account/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
