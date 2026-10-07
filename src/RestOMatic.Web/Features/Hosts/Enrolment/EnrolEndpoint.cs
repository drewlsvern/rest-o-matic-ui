using Microsoft.EntityFrameworkCore;
using RestOMatic.Web.Features.Hosts.Contract;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Features.Hosts.Enrolment;

/// <summary>
/// <c>POST /api/v1/enrol</c>: a host spends its one-time token and gets its
/// credential. No authentication; the token in the body is the proof.
/// </summary>
public static class EnrolEndpoint
{
    public const string Path = "/api/v1/enrol";
    public const string RateLimitPolicy = "enrol";

    /// <summary>An enrol request is a few hundred bytes; nothing larger is read.</summary>
    public const long MaxRequestBytes = 64 * 1024;

    public static RouteHandlerBuilder MapEnrol(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(Path, async (HttpContext context, EnrolHandler handler, CancellationToken cancellationToken) =>
            {
                var read = await ContractRequests.ReadAsync<EnrolRequest>(context.Request, MaxRequestBytes, cancellationToken);
                return read.Error ?? await handler.EnrolAsync(read.Message!);
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy);
}

public sealed class EnrolHandler(AppDbContext db, TimeProvider time)
{
    /// <summary>The same reply whether the token was never issued, used, replaced or expired.</summary>
    public const string TokenRejectedMessage = "The enrol token is unknown, already used or expired. Issue a new enrol command for this host.";

    public async Task<IResult> EnrolAsync(EnrolRequest request)
    {
        var now = time.GetUtcNow();
        var hash = Secrets.HashEnrolToken(request.Token);
        var host = await db.Set<Host>().FirstOrDefaultAsync(h => h.EnrolTokenHash == hash);
        if (host is null || host.EnrolTokenExpiresAt is not { } expiresAt || expiresAt <= now)
        {
            return ContractErrors.Reply(StatusCodes.Status401Unauthorized, ContractErrors.TokenRejected, TokenRejectedMessage);
        }

        var credential = Secrets.NewCredential();
        host.Enrol(Secrets.HashCredential(credential), request.PublicKey, request.Host.ToDescription(), now);
        await db.SaveChangesAsync();

        return Results.Json(
            new EnrolResponse(ContractJson.FormatVersion, host.Id.ToString(), host.Name, credential, RecoveryRecipients: []),
            ContractJson.Options);
    }
}
