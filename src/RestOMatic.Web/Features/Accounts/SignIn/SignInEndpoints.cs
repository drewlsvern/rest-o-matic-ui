using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using RestOMatic.Web.Features.Accounts.Sessions;

namespace RestOMatic.Web.Features.Accounts.SignIn;

/// <summary>The plain HTTP requests of signing in and out, which can set and clear the cookie.</summary>
public static class SignInEndpoints
{
    public const string SignInPath = "/account/sign-in";
    public const string CompletePath = "/account/complete";
    public const string SignOutPath = "/account/sign-out";

    public static void MapSignIn(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(CompletePath, CompleteAsync).AllowAnonymous();

        endpoints.MapPost(SignOutPath, async (HttpContext context, IAntiforgery antiforgery) =>
            {
                // The antiforgery middleware only records the outcome for an
                // endpoint that binds no form. Checking it here stops a form
                // on another site from signing anyone out.
                if (!await antiforgery.IsRequestValidAsync(context))
                {
                    return Results.BadRequest();
                }
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect(SignInPath);
            })
            .AllowAnonymous()
            .DisableAntiforgery();
    }

    private static async Task<IResult> CompleteAsync(
        HttpContext context,
        [FromQuery] string? ticket,
        SignInTickets tickets,
        UserAccounts accounts,
        AccountSessions sessions)
    {
        var redeemed = tickets.Redeem(ticket, context.Request.Cookies[BrowserBinding.CookieName]);
        var user = redeemed is null ? null : await accounts.FindUserAsync(redeemed.Value.UserId);
        if (redeemed is null || user is null)
        {
            return Results.LocalRedirect(SignInPath);
        }

        await sessions.SignInAsync(context, user);
        BrowserBinding.Clear(context);
        return Results.LocalRedirect(redeemed.Value.ReturnUrl);
    }
}

/// <summary>
/// A random value in a <c>SameSite=Strict</c> cookie, set when a page that
/// signs people in is first loaded. A sign-in ticket is only accepted with
/// the same value, so a completion link sent from another site, or to
/// another browser, does not sign anyone in.
/// </summary>
public static class BrowserBinding
{
    public const string CookieName = "rom.signin";

    private const string ItemKey = "rom.signin";

    /// <summary>Middleware: makes sure the pages that sign people in have a binding to hand to the circuit.</summary>
    public static async Task EnsureAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path;
        if (HttpMethods.IsGet(context.Request.Method)
            && (path.Equals(SignInEndpoints.SignInPath, StringComparison.OrdinalIgnoreCase)
                || path.Equals(Setup.SetupPage.Path, StringComparison.OrdinalIgnoreCase)))
        {
            var value = context.Request.Cookies[CookieName];
            if (string.IsNullOrEmpty(value))
            {
                value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                context.Response.Cookies.Append(CookieName, value, Options(context));
            }
            context.Items[ItemKey] = value;
        }
        await next(context);
    }

    /// <summary>The value for this request, while a page is being prerendered.</summary>
    public static string? For(HttpContext context) =>
        context.Items[ItemKey] as string ?? context.Request.Cookies[CookieName];

    public static void Clear(HttpContext context) => context.Response.Cookies.Delete(CookieName, Options(context));

    private static CookieOptions Options(HttpContext context) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = context.Request.IsHttps || !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        Path = "/",
        MaxAge = TimeSpan.FromHours(1),
    };
}
