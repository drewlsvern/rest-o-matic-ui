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

    public static void UseShell(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

        // No HTTPS redirection and no HSTS: the app serves plain HTTP and TLS
        // is provided by whatever sits in front of it on the tailnet.

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
