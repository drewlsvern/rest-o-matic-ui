using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestOMatic.Web.Features.Hosts.Contract;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Features.Hosts.CheckIn;

/// <summary>
/// <c>Authorization: Bearer &lt;credential&gt;</c> from an enrolled host. Only
/// the host endpoints use this scheme, and they use nothing else, so a
/// host's credential opens no page and a user's session sends no check-in.
/// </summary>
public sealed class HostCredentialAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "HostCredential";
    public const string Policy = "Host";

    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = Secrets.HashCredential(header[BearerPrefix.Length..].Trim());
        var hostId = await db.Set<Host>().Where(h => h.CredentialHash == hash).Select(h => (Guid?)h.Id).FirstOrDefaultAsync();
        if (hostId is null)
        {
            return AuthenticateResult.Fail("The credential matches no enrolled host.");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, hostId.Value.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>The contract's 401, never a redirect.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ContractErrors.WriteAsync(Response, StatusCodes.Status401Unauthorized, ContractErrors.CredentialRejected,
            "The credential is unknown. If this host was removed, enrol it again.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ContractErrors.WriteAsync(Response, StatusCodes.Status403Forbidden, ContractErrors.CredentialRejected,
            "This credential is not allowed here.");

    public static Guid HostId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
