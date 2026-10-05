using Microsoft.AspNetCore.Authorization;
using MudBlazor.Services;

namespace RestOMatic.Web.Shell;

public static class ShellRegistration
{
    public static void AddShell(this IHostApplicationBuilder builder)
    {
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddMudServices();
    }

    /// <summary>Error handling, early in the pipeline.</summary>
    public static void UseShell(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

        // No HTTPS redirection and no HSTS: the app serves plain HTTP and TLS
        // is provided by whatever sits in front of it on the tailnet.
    }

    /// <summary>The pages and their assets. After authentication and authorization.</summary>
    public static void MapShell(this WebApplication app)
    {
        app.UseAntiforgery();

        // Needed before anyone has signed in: the sign-in page's styles,
        // scripts and logo.
        app.MapStaticAssets().AllowAnonymous();

        // The framework's script (/_framework, served from here outside
        // Development) and the circuit's endpoints (/_blazor) must be open so
        // the sign-in page can run. Pages are still checked one by one, by the
        // fallback policy and their [Authorize]/[AllowAnonymous] attributes.
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .Add(endpoint =>
            {
                var pattern = (endpoint as RouteEndpointBuilder)?.RoutePattern.RawText?.TrimStart('/');
                if (pattern is not null
                    && (pattern.StartsWith("_blazor", StringComparison.Ordinal)
                        || pattern.StartsWith("_framework/", StringComparison.Ordinal)))
                {
                    endpoint.Metadata.Add(new AllowAnonymousAttribute());
                }
            });
    }
}
