using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace RestOMatic.Web.Features.Accounts.Sessions;

/// <summary>
/// Holds every session on the server. The browser's cookie carries only the
/// session's key, so ending a session here ends it for good, and a restart
/// ends them all.
/// </summary>
public sealed class SessionStore(TimeProvider time) : ITicketStore
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromHours(12);
    public static readonly TimeSpan AbsoluteLimit = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.Ordinal);

    /// <summary>Raised with a session's key when it ends, for whatever reason.</summary>
    public event Action<string>? SessionEnded;

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        RemoveIdle();
        var key = AccountClaims.SessionId(ticket.Principal)
            ?? throw new InvalidOperationException("A session must be started by AccountSessions.");
        _sessions[key] = new Entry(ticket, time.GetUtcNow());
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (_sessions.ContainsKey(key))
        {
            _sessions[key] = new Entry(ticket, time.GetUtcNow());
        }
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!_sessions.TryGetValue(key, out var entry))
        {
            return Task.FromResult<AuthenticationTicket?>(null);
        }
        var now = time.GetUtcNow();
        if (now - entry.LastSeen > IdleLimit)
        {
            End(key);
            return Task.FromResult<AuthenticationTicket?>(null);
        }
        _sessions[key] = entry with { LastSeen = now };
        return Task.FromResult<AuthenticationTicket?>(entry.Ticket);
    }

    public Task RemoveAsync(string key)
    {
        End(key);
        return Task.CompletedTask;
    }

    public bool IsActive(string key) =>
        _sessions.TryGetValue(key, out var entry) && time.GetUtcNow() - entry.LastSeen <= IdleLimit;

    /// <summary>Ends every session the user has, except <paramref name="except"/>.</summary>
    public void EndSessionsOf(Guid userId, string? except = null)
    {
        foreach (var (key, entry) in _sessions)
        {
            if (key != except && AccountClaims.UserId(entry.Ticket.Principal) == userId)
            {
                End(key);
            }
        }
    }

    /// <summary>Keeps a session valid after its user's security stamp changed because of something done in it.</summary>
    public void UpdateStamp(string key, Guid stamp)
    {
        if (!_sessions.TryGetValue(key, out var entry))
        {
            return;
        }
        var identity = new ClaimsIdentity(
            entry.Ticket.Principal.Claims.Where(c => c.Type != AccountClaims.Stamp)
                .Append(new Claim(AccountClaims.Stamp, stamp.ToString())),
            entry.Ticket.Principal.Identity?.AuthenticationType);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), entry.Ticket.Properties, entry.Ticket.AuthenticationScheme);
        _sessions[key] = entry with { Ticket = ticket };
    }

    /// <summary>The stamp a session currently carries, which can be newer than the one its circuit started with.</summary>
    public Guid? StampOf(string key) =>
        _sessions.TryGetValue(key, out var entry) ? AccountClaims.SecurityStamp(entry.Ticket.Principal) : null;

    private void End(string key)
    {
        if (_sessions.TryRemove(key, out _))
        {
            SessionEnded?.Invoke(key);
        }
    }

    private void RemoveIdle()
    {
        var now = time.GetUtcNow();
        foreach (var (key, entry) in _sessions)
        {
            if (now - entry.LastSeen > IdleLimit)
            {
                End(key);
            }
        }
    }

    private sealed record Entry(AuthenticationTicket Ticket, DateTimeOffset LastSeen);
}

/// <summary>The claims every session carries. Profile details are not among them, because they change.</summary>
public static class AccountClaims
{
    public const string AuthenticationType = "rest-o-matic";
    public const string Session = "sid";
    public const string Stamp = "stamp";
    public const string StartedAt = "auth_time";

    public static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string? SessionId(ClaimsPrincipal principal) => principal.FindFirstValue(Session);

    public static Guid? SecurityStamp(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(Stamp), out var stamp) ? stamp : null;

    public static DateTimeOffset? Started(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(StartedAt), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
