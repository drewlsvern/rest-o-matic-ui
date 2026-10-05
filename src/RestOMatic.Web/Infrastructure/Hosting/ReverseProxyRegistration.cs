using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace RestOMatic.Web.Infrastructure.Hosting;

/// <summary>
/// TLS is terminated by a reverse proxy (Caddy) in front of the app. The
/// proxy's <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are trusted
/// only from the addresses listed under <c>ReverseProxy</c>, and from nobody
/// when none are listed.
/// </summary>
public static class ReverseProxyRegistration
{
    public const string ConfigurationSection = "ReverseProxy";

    public static void AddReverseProxy(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(ConfigurationSection);
        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

        var settings = new ReverseProxySettings(
            [.. proxies.Select(value => Parse(value, (string text, out IPAddress? result) => IPAddress.TryParse(text, out result)))],
            [.. networks.Select(value => Parse(value, (string text, out System.Net.IPNetwork result) => System.Net.IPNetwork.TryParse(text, out result)))]);
        builder.Services.AddSingleton(settings);

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // The framework trusts loopback unless told otherwise. Nothing is
            // trusted here unless it is configured.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in settings.Proxies)
            {
                options.KnownProxies.Add(proxy);
            }
            foreach (var network in settings.Networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });
    }

    /// <summary>Must be the first middleware, so everything after it sees the client's address and scheme.</summary>
    public static void UseReverseProxy(this WebApplication app)
    {
        // With both lists empty the middleware would accept the headers from
        // any sender, so it is left out entirely.
        if (app.Services.GetRequiredService<ReverseProxySettings>().IsConfigured)
        {
            app.UseForwardedHeaders();
        }
    }

    private delegate bool TryParse<T>(string value, out T result);

    private static T Parse<T>(string value, TryParse<T?> tryParse) =>
        tryParse(value.Trim(), out var result) && result is not null
            ? result
            : throw new InvalidOperationException(
                $"'{value}' under {ConfigurationSection} is not a valid IP address or network.");

    private sealed record ReverseProxySettings(IReadOnlyList<IPAddress> Proxies, IReadOnlyList<System.Net.IPNetwork> Networks)
    {
        public bool IsConfigured => Proxies.Count > 0 || Networks.Count > 0;
    }
}
