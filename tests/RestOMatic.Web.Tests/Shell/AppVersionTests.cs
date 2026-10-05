using RestOMatic.Web.Shell;

namespace RestOMatic.Web.Tests.Shell;

public class AppVersionTests
{
    [Fact]
    public async Task Page_shows_the_development_version_when_the_build_was_given_none()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();

        var html = await client.GetStringAsync("/");

        // The tests are always built without a version.
        Assert.Equal("0.0.0-dev", AppVersion.Current);
        Assert.Contains("0.0.0-dev", html);
        Assert.DoesNotContain("0.0.0-dev+", html);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+0123abc", "1.2.3")]
    [InlineData("1.2.3-rc.1+0123abc", "1.2.3-rc.1")]
    [InlineData("0.0.0-dev+0123abc", "0.0.0-dev")]
    public void Commit_suffix_is_dropped(string informationalVersion, string expected)
    {
        Assert.Equal(expected, AppVersion.FromInformationalVersion(informationalVersion));
    }
}
