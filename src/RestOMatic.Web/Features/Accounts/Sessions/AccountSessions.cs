using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Features.Accounts.Sessions;

/// <summary>
/// Where every sign-in method ends: given a user it already trusts, it
/// starts a session and sets the cookie. Password sign-in reaches it through
/// a one-time ticket; a future OIDC callback would call it directly.
/// </summary>
public sealed class AccountSessions(TimeProvider time)
{
    public const string CookieName = "rom.session";

    public async Task SignInAsync(HttpContext context, User user)
    {
        var sessionId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(AccountClaims.Session, sessionId),
                new Claim(AccountClaims.Stamp, user.SecurityStamp.ToString()),
                new Claim(AccountClaims.StartedAt, time.GetUtcNow().ToUnixTimeSeconds().ToString()),
            ],
            AccountClaims.AuthenticationType);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>
    /// Run on every request that carries a session: refuses sessions past
    /// the absolute limit and sessions whose user has since changed their
    /// security stamp (password changed or reset) or been removed.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var store = services.GetRequiredService<SessionStore>();

        var sessionId = principal is null ? null : AccountClaims.SessionId(principal);
        var userId = principal is null ? null : AccountClaims.UserId(principal);
        var stamp = principal is null ? null : AccountClaims.SecurityStamp(principal);
        var started = principal is null ? null : AccountClaims.Started(principal);

        var valid = sessionId is not null && userId is not null && stamp is not null && started is not null
            && now - started.Value <= SessionStore.AbsoluteLimit
            && await services.GetRequiredService<AppDbContext>().Set<User>()
                .AnyAsync(u => u.Id == userId && u.SecurityStamp == stamp);

        if (!valid)
        {
            context.RejectPrincipal();
            if (sessionId is not null)
            {
                await store.RemoveAsync(sessionId);
            }
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>
    /// Pages are sent to the sign-in page. Anything else that needs a user
    /// (an API call, a picture, a form post) is told 401, because a redirect
    /// to an HTML page would only confuse it.
    /// </summary>
    public static Task RedirectToSignInAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        var request = context.Request;
        var wantsPage = HttpMethods.IsGet(request.Method)
            && request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
        if (wantsPage)
        {
            context.Response.Redirect(context.RedirectUri);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }
        return Task.CompletedTask;
    }
}
