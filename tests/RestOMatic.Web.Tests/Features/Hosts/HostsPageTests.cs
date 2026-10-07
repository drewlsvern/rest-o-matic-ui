using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using RestOMatic.Web.Features.Hosts;
using RestOMatic.Web.Features.Hosts.Pages;

namespace RestOMatic.Web.Tests.Features.Hosts;

public class HostsPageTests
{
    [Fact]
    public async Task The_page_shows_an_enrolled_hosts_details()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync("backup-box");
        await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-full", hostId).ToJsonString());
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        var html = await PageAsync(client);

        Assert.Contains("backup-box", html);
        Assert.Contains("prd-podman-01", html);
        Assert.Contains("v0.2.0", html);
        Assert.Contains("0.17.3", html);
        Assert.Contains("just now", html);
        var jobs = HostsTestSupport.CheckIn("checkin-full", hostId)["parts"]!["status"]!["content"]!["jobs"]!.AsArray().Count;
        Assert.Contains($">{jobs}</td>", html.Replace(" ", ""));
    }

    [Fact]
    public async Task A_waiting_host_shows_when_its_command_expires_and_never_its_token()
    {
        using var factory = new AppFactory();
        var (_, token) = await factory.AddHostAsync("new-box");
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        var html = await PageAsync(client);

        Assert.Contains("new-box", html);
        Assert.Contains("Waiting to enrol", html);
        Assert.Contains("command valid until", html);
        Assert.DoesNotContain(token, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hosts_are_sorted_by_hostname_with_waiting_hosts_last()
    {
        using var factory = new AppFactory();
        var (_, waiting) = await factory.AddHostAsync("aaa-waiting");
        var (_, zeta) = await factory.AddHostAsync("first-added");
        await factory.EnrolAsync(zeta, hostname: "zeta-server");
        var (_, alpha) = await factory.AddHostAsync("second-added");
        await factory.EnrolAsync(alpha, hostname: "alpha-server");
        using var client = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        var html = await PageAsync(client);

        var order = new[] { "alpha-server", "zeta-server", "aaa-waiting" }.Select(text => html.IndexOf(text, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
    }

    [Fact]
    public async Task The_page_needs_a_signed_in_user()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        using var client = factory.CreateDirectClient();

        using var response = await AppFactory.GetPageAsync(client, "/hosts");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task The_command_uses_PublicUrl_when_it_is_set()
    {
        await using var context = CommandContext(publicUrl: "https://backups.internal/");

        var command = RenderCommand(context);

        Assert.Equal("rest-o-matic enrol https://backups.internal --token ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            command.Find("[data-testid='enrol-command']").TextContent);
        Assert.DoesNotContain("plain HTTP", command.Markup);
    }

    [Fact]
    public async Task Without_PublicUrl_the_command_uses_the_browsers_address_and_warns_about_plain_http()
    {
        await using var context = CommandContext(publicUrl: null);

        var command = RenderCommand(context);

        Assert.Equal("rest-o-matic enrol http://localhost --token ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            command.Find("[data-testid='enrol-command']").TextContent);
        Assert.Contains("--allow-http", command.Markup);
    }

    [Theory]
    [InlineData(null, "https://backups.example.com/", "https://backups.example.com")]
    [InlineData("", "https://backups.example.com/", "https://backups.example.com")]
    [InlineData("https://backups.internal", "http://localhost:8180/", "https://backups.internal")]
    public void Address_for_the_command(string? publicUrl, string baseUri, string expected)
    {
        Assert.Equal(expected, EnrolCommand.AddressFor(publicUrl, baseUri));
    }

    private static BunitContext CommandContext(string? publicUrl)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        context.Services.AddSingleton(Options.Create(new HostsOptions { PublicUrl = publicUrl }));
        return context;
    }

    private static IRenderedComponent<EnrolCommand> RenderCommand(BunitContext context) =>
        context.Render<EnrolCommand>(parameters => parameters
            .Add(p => p.HostName, "prd-podman-01")
            .Add(p => p.Token, new IssuedToken("ABCDEFGHIJKLMNOPQRSTUVWXYZ", DateTimeOffset.UtcNow.AddHours(24))));

    private static async Task<string> PageAsync(HttpClient client)
    {
        using var response = await AppFactory.GetPageAsync(client, "/hosts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
