using System.Net;

namespace RestOMatic.Web.Tests.Features.Home;

public class HomePageTests
{
    [Fact]
    public async Task Home_page_loads_and_shows_the_app_name_when_signed_in()
    {
        using var factory = new AppFactory();
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        var response = await AppFactory.GetPageAsync(client, "/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("rest-o-matic", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Plain_http_is_answered_and_not_redirected_to_https()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();

        var response = await client.GetAsync("http://localhost/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
