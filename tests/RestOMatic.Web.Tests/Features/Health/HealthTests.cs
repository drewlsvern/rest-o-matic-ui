using System.Net;
using Microsoft.Data.Sqlite;
using RestOMatic.Web.Features.Health;

namespace RestOMatic.Web.Tests.Features.Health;

public class HealthTests
{
    [Fact]
    public async Task Healthy_when_the_database_can_be_opened()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();

        var response = await client.GetAsync(HealthFeature.Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unavailable_when_the_database_cannot_be_opened()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(HealthFeature.Path)).StatusCode);

        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(factory.DataDirectory, "*.db*"))
        {
            File.Delete(file);
        }

        var response = await client.GetAsync(HealthFeature.Path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
