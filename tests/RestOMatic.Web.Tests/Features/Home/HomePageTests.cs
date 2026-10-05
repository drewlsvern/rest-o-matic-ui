using System.Net;

namespace RestOMatic.Web.Tests.Features.Home;

public class HomePageTests
{
    [Fact]
    public async Task Home_page_loads_and_shows_the_app_name()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("rest-o-matic", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Plain_http_is_answered_and_not_redirected()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();

        var response = await client.GetAsync("http://localhost/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
