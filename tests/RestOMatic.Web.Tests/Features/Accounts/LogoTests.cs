using System.Net;
using System.Xml.Linq;

namespace RestOMatic.Web.Tests.Features.Accounts;

public class LogoTests
{
    [Theory]
    [InlineData("/images/rest-o-matic.svg")]
    [InlineData("/images/rest-o-matic-mark.svg")]
    public async Task The_logo_is_served_by_the_app_and_refers_to_nothing_else(string path)
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();

        using var response = await client.GetAsync(path);
        var svg = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = XDocument.Parse(svg);
        var references = document.Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is "href" or "src" || attribute.Value.Contains("url(", StringComparison.Ordinal));
        Assert.Empty(references);
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName is "script" or "image" or "foreignObject");
    }

    [Theory]
    [InlineData("/images/rest-o-matic.svg")]
    [InlineData("/images/rest-o-matic-mark.svg")]
    public async Task The_logo_does_not_mention_restic(string path)
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();

        var svg = await client.GetStringAsync(path);

        Assert.DoesNotContain("restic", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rest-o-matic", svg);
    }
}
