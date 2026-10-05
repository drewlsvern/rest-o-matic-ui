using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestOMatic.Web.Features.Accounts.Profile;
using RestOMatic.Web.Features.Accounts.Sessions;
using RestOMatic.Web.Features.Accounts.Setup;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Features.Accounts;

public static class AccountsRegistration
{
    public static void AddAccounts(this IHostApplicationBuilder builder)
    {
        // A command run in the container prints only its own result, not the
        // app's startup log. (A minimum level would not do: appsettings.json
        // sets its own.)
        if (Environment.GetCommandLineArgs().ElementAtOrDefault(1) == ResetPasswordCommand.Name)
        {
            builder.Logging.ClearProviders();
        }

        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SessionStore>();
        services.AddSingleton<SignInThrottle>();
        services.AddSingleton<SignInTickets>();
        services.AddSingleton<FirstRun>();
        services.AddSingleton<AccountEvents>();
        services.AddSingleton<Passwords>();
        services.AddSingleton<AccountSessions>();
        services.AddScoped<UserAccounts>();
        services.AddScoped<SignInHandler>();
        services.AddScoped<SetupHandler>();

        // The ways to sign in, in the order the sign-in page shows them.
        services.AddSingleton(new SignInMethod("password", 0, typeof(PasswordSignInForm)));

        var isDevelopment = builder.Environment.IsDevelopment();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = AccountSessions.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                // Plain HTTP only works for `dotnet run`; everywhere else TLS
                // is in front of the app.
                options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = SessionStore.IdleLimit;
                options.SlidingExpiration = true;
                options.LoginPath = SignInEndpoints.SignInPath;
                options.ReturnUrlParameter = "returnUrl";
                options.Events.OnValidatePrincipal = AccountSessions.ValidateAsync;
                options.Events.OnRedirectToLogin = AccountSessions.RedirectToSignInAsync;
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<SessionStore, TimeProvider>((options, store, time) =>
            {
                options.SessionStore = store;
                options.TimeProvider = time;
            });

        // Everything needs a signed-in user unless it says otherwise,
        // including endpoints that later changes add.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();
    }

    /// <summary>After the reverse proxy and error handling, before antiforgery and the endpoints.</summary>
    public static void UseAccounts(this WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var hasUsers = scope.ServiceProvider.GetRequiredService<UserAccounts>().AnyAsync().GetAwaiter().GetResult();
            app.Services.GetRequiredService<FirstRun>().Start(hasUsers);
        }

        var firstRun = app.Services.GetRequiredService<FirstRun>();
        app.UseAuthentication();
        app.Use(firstRun.RedirectAsync);
        app.Use(BrowserBinding.EnsureAsync);
        app.UseAuthorization();
    }

    public static void MapAccounts(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSignIn();
        endpoints.MapPictures();
    }

    /// <summary>
    /// Runs a command given on the command line instead of the web server.
    /// Returns false when the arguments are not an accounts command.
    /// </summary>
    public static async Task<bool> RunAccountsCommandAsync(this WebApplication app, string[] args)
    {
        if (args.Length == 0 || args[0] != ResetPasswordCommand.Name)
        {
            return false;
        }
        Environment.ExitCode = await ResetPasswordCommand.RunAsync(app.Services, args[1..], Console.Out, Console.Error);
        return true;
    }
}
