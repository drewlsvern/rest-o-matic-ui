using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RestOMatic.Web.Features.Health;

namespace RestOMatic.Web.Tests.Infrastructure.Hosting;

public class ReverseProxyTests
{
    private const string Proxy = "10.0.0.1";

    [Fact]
    public async Task Forwarded_headers_from_a_configured_proxy_are_used()
    {
        var seen = new Seen();
        using var factory = Start(seen, ("ReverseProxy:KnownProxies:0", Proxy));

        await Send(factory, from: Proxy);

        Assert.Equal("https", seen.Scheme);
        Assert.Equal(IPAddress.Parse("100.64.0.7"), seen.Client);
    }

    [Fact]
    public async Task A_configured_IPv4_proxy_matches_its_IPv6_mapped_form()
    {
        // Kestrel listens on IPv4 and IPv6 at once, so a proxy in the same pod
        // connecting to 127.0.0.1 arrives as ::ffff:127.0.0.1.
        var seen = new Seen();
        using var factory = Start(seen, ("ReverseProxy:KnownProxies:0", "127.0.0.1"));

        await Send(factory, from: "::ffff:127.0.0.1");

        Assert.Equal("https", seen.Scheme);
        Assert.Equal(IPAddress.Parse("100.64.0.7"), seen.Client);
    }

    [Fact]
    public async Task Forwarded_headers_from_a_configured_network_are_used()
    {
        var seen = new Seen();
        using var factory = Start(seen, ("ReverseProxy:KnownNetworks:0", "10.0.0.0/8"));

        await Send(factory, from: "10.1.2.3");

        Assert.Equal("https", seen.Scheme);
        Assert.Equal(IPAddress.Parse("100.64.0.7"), seen.Client);
    }

    [Fact]
    public async Task Forwarded_headers_from_any_other_address_are_ignored()
    {
        var seen = new Seen();
        using var factory = Start(seen, ("ReverseProxy:KnownProxies:0", Proxy));

        await Send(factory, from: "10.0.0.2");

        Assert.Equal("http", seen.Scheme);
        Assert.Equal(IPAddress.Parse("10.0.0.2"), seen.Client);
    }

    [Theory]
    [InlineData("10.0.0.2")]
    [InlineData("127.0.0.1")]
    public async Task Forwarded_headers_are_ignored_when_no_proxy_is_configured(string from)
    {
        var seen = new Seen();
        using var factory = Start(seen);

        await Send(factory, from);

        Assert.Equal("http", seen.Scheme);
        Assert.Equal(IPAddress.Parse(from), seen.Client);
    }

    [Fact]
    public void An_invalid_address_stops_the_app_with_a_message_naming_it()
    {
        using var factory = Start(new Seen(), ("ReverseProxy:KnownProxies:0", "not-an-address"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("not-an-address", error.Message);
    }

    private static async Task Send(AppFactory factory, string from)
    {
        using var client = factory.CreateDirectClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, HealthFeature.Path);
        request.Headers.Add(Probe.RemoteAddressHeader, from);
        request.Headers.Add("X-Forwarded-For", "100.64.0.7");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static AppFactory Start(Seen seen, params (string Key, string Value)[] settings) =>
        new(configure: builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new Probe(seen)));
        });

    private sealed class Seen
    {
        public string? Scheme { get; set; }

        public IPAddress? Client { get; set; }
    }

    /// <summary>
    /// The test server has no real connection, so this sets the address a
    /// request comes from, then records what the app made of it.
    /// </summary>
    private sealed class Probe(Seen seen) : IStartupFilter
    {
        public const string RemoteAddressHeader = "X-Test-Remote-Address";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers[RemoteAddressHeader]!);
                await nextMiddleware();
                seen.Scheme = context.Request.Scheme;
                seen.Client = context.Connection.RemoteIpAddress;
            });
            next(app);
        };
    }
}
