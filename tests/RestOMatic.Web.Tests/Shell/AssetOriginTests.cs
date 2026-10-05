using System.Text.RegularExpressions;

namespace RestOMatic.Web.Tests.Shell;

public partial class AssetOriginTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/account/sign-in")]
    [InlineData("/setup")]
    public async Task Page_references_nothing_on_another_origin(string page)
    {
        using var factory = new AppFactory();
        using var client = page == "/"
            ? await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"))
            : factory.CreateDirectClient();

        var html = await client.GetStringAsync(page);

        var references = ReferenceAttribute().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(references);
        Assert.DoesNotContain(references, IsAnotherOrigin);
        Assert.DoesNotContain("fonts.googleapis.com", html);
    }

    [Fact]
    public async Task Stylesheets_import_nothing_from_another_origin()
    {
        using var factory = new AppFactory();
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));
        var html = await client.GetStringAsync("/");
        var stylesheets = StylesheetLink().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(stylesheets);

        foreach (var stylesheet in stylesheets)
        {
            var css = await client.GetStringAsync(stylesheet);
            Assert.DoesNotMatch(ExternalCssReference(), css);
        }
    }

    private static bool IsAnotherOrigin(string reference) =>
        reference.StartsWith("//", StringComparison.Ordinal)
        || reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("""\b(?:src|href)\s*=\s*"([^"]*)" """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex ReferenceAttribute();

    [GeneratedRegex("""<link[^>]*rel="stylesheet"[^>]*href="([^"]*)" """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex StylesheetLink();

    [GeneratedRegex("""(?:@import|url\()\s*['"]?(?:https?:)?//""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalCssReference();
}
