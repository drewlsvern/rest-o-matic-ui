using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.EntityFrameworkCore;
using RestOMatic.Web.Features.Hosts.Contract;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Features.Hosts.CheckIn;

/// <summary><c>POST /api/v1/checkin</c>, sent by an enrolled host at the start of every tick.</summary>
public static class CheckInEndpoint
{
    public const string Path = "/api/v1/checkin";

    public static RouteHandlerBuilder MapCheckIn(this IEndpointRouteBuilder endpoints, long maxRequestBytes) =>
        endpoints.MapPost(Path, async (HttpContext context, CheckInHandler handler, CancellationToken cancellationToken) =>
            {
                var hostId = HostCredentialAuthenticationHandler.HostId(context.User);
                var read = await ContractRequests.ReadAsync<CheckInRequest>(context.Request, maxRequestBytes, cancellationToken);
                if (read.Error is not null)
                {
                    if (read.ErrorStatus == StatusCodes.Status413PayloadTooLarge)
                    {
                        await handler.LogTooLargeAsync(hostId, maxRequestBytes);
                    }
                    return read.Error;
                }
                return await handler.CheckInAsync(hostId, read.Message!, cancellationToken);
            })
            .RequireAuthorization(HostCredentialAuthenticationHandler.Policy)
            .WithMetadata(new RequestSizeLimit(maxRequestBytes));
}

/// <summary>
/// Keeps the latest copy of each part a host sends, and tells it which parts
/// to send again. Fingerprints are the host's and are only compared, never
/// computed.
/// </summary>
public sealed class CheckInHandler(AppDbContext db, TimeProvider time, CheckInLocks locks, ILogger<CheckInHandler> logger)
{
    public async Task<IResult> CheckInAsync(Guid hostId, CheckInRequest request, CancellationToken cancellationToken)
    {
        // A credential and an ID that disagree: the contract's advice for
        // credential_rejected, enrol again, is the right one.
        if (request.HostId is not null && (!Guid.TryParse(request.HostId, out var claimed) || claimed != hostId))
        {
            return CredentialRejected("The host_id does not belong to this credential. Enrol the host again.");
        }

        // Two overlapping check-ins from one host must not interleave their
        // compare-and-keep of sent_at. One process, so a lock here is enough.
        using var _ = await locks.AcquireAsync(hostId, cancellationToken);

        var host = await db.Set<Host>().Include(h => h.Parts).FirstOrDefaultAsync(h => h.Id == hostId, cancellationToken);
        if (host is null)
        {
            return CredentialRejected("This host was removed. Enrol it again.");
        }

        var now = time.GetUtcNow();
        host.CheckedIn(request.Host.ToDescription(), now);

        var parts = request.Parts;
        if (parts.Status.Content is { } status && Keep(host, PartKinds.Status, parts.Status.Fingerprint, status.GetRawText(), request.SentAt, now))
        {
            host.JobCount = CountJobs(status);
        }
        if (parts.Snapshots.Content is { } snapshots)
        {
            Keep(host, PartKinds.Snapshots, parts.Snapshots.Fingerprint, snapshots.GetRawText(), request.SentAt, now);
        }
        if (parts.Config.Withheld is { } withheld)
        {
            Withhold(host, parts.Config.Fingerprint, withheld, request.SentAt, now);
        }
        else if (parts.Config.Content is { } config)
        {
            Keep(host, PartKinds.Config, parts.Config.Fingerprint, config, request.SentAt, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.Json(
            new CheckInResponse(ContractJson.FormatVersion, ContractJson.Time(now), Resend(host, request), Config: null, Actions: []),
            ContractJson.Options);
    }

    public async Task LogTooLargeAsync(Guid hostId, long maxRequestBytes)
    {
        var name = await db.Set<Host>().Where(h => h.Id == hostId).Select(h => h.Name).FirstOrDefaultAsync();
        logger.LogWarning(
            "A check-in from host {HostName} ({HostId}) was refused: it is larger than CheckIn:MaxRequestBytes ({MaxRequestBytes} bytes). Raise the setting if this host's snapshot lists are expected to be this large.",
            name, hostId, maxRequestBytes);
    }

    /// <summary>Keeps the content unless a copy from a later check-in is already held. Returns whether it was kept.</summary>
    private static bool Keep(Host host, string kind, string fingerprint, string content, DateTimeOffset sentAt, DateTimeOffset now)
    {
        var part = host.Parts.FirstOrDefault(p => p.Kind == kind);
        if (part is not null && part.SentAt > sentAt)
        {
            return false;
        }
        if (part is null)
        {
            part = new HostPart { HostId = host.Id, Kind = kind };
            host.Parts.Add(part);
        }
        part.Fingerprint = fingerprint;
        part.Content = content;
        part.SentAt = sentAt;
        part.ReceivedAt = now;
        part.IsCurrent = true;
        part.WithheldReason = null;
        part.WithheldFields = null;
        return true;
    }

    /// <summary>
    /// The host's current config holds a plain-text secret, so it is not
    /// sent. The last config text received is kept, marked as not current.
    /// </summary>
    private static void Withhold(Host host, string fingerprint, Withheld withheld, DateTimeOffset sentAt, DateTimeOffset now)
    {
        var part = host.Parts.FirstOrDefault(p => p.Kind == PartKinds.Config);
        if (part is not null && part.SentAt > sentAt)
        {
            return;
        }
        if (part is null)
        {
            part = new HostPart { HostId = host.Id, Kind = PartKinds.Config, Fingerprint = fingerprint };
            host.Parts.Add(part);
        }
        part.SentAt = sentAt;
        part.ReceivedAt = now;
        part.IsCurrent = false;
        part.WithheldReason = withheld.Reason;
        part.WithheldFields = JsonSerializer.Serialize(withheld.Fields);
    }

    /// <summary>
    /// Every part whose reported fingerprint differs from the copy held,
    /// including parts with no copy at all: covers a lost database and a
    /// restored older one. Not a withheld config, which cannot be sent, and
    /// not a part held from a later check-in than this one.
    /// </summary>
    private static List<string> Resend(Host host, CheckInRequest request)
    {
        List<string> resend = [];
        foreach (var (kind, fingerprint) in new[]
                 {
                     (PartKinds.Status, request.Parts.Status.Fingerprint),
                     (PartKinds.Snapshots, request.Parts.Snapshots.Fingerprint),
                     (PartKinds.Config, request.Parts.Config.Fingerprint),
                 })
        {
            if (kind == PartKinds.Config && request.Parts.Config.Withheld is not null)
            {
                continue;
            }
            var part = host.Parts.FirstOrDefault(p => p.Kind == kind);
            if (part is not null && part.SentAt > request.SentAt)
            {
                continue;
            }
            if (part?.Content is null || part.Fingerprint != fingerprint)
            {
                resend.Add(kind);
            }
        }
        return resend;
    }

    private static int? CountJobs(JsonElement status) =>
        status.ValueKind == JsonValueKind.Object
        && status.TryGetProperty("jobs", out var jobs)
        && jobs.ValueKind == JsonValueKind.Array
            ? jobs.GetArrayLength()
            : null;

    private static IResult CredentialRejected(string message) =>
        ContractErrors.Reply(StatusCodes.Status401Unauthorized, ContractErrors.CredentialRejected, message);
}

/// <summary>
/// The endpoint's body size limit. Routing applies it to the request, and
/// request decompression to the decompressed body.
/// </summary>
internal sealed class RequestSizeLimit(long maxRequestBodySize) : IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize => maxRequestBodySize;
}

/// <summary>One lock per host, so a host's overlapping check-ins are handled one at a time.</summary>
public sealed class CheckInLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(hostId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Release(semaphore);
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
