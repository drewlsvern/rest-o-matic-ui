using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace RestOMatic.Web.Features.Accounts.SignIn;

/// <summary>
/// Slows down guessing. Per user name: after 5 failures in a row, attempts
/// for that name are refused for 30 seconds, doubling with each further
/// failure up to 15 minutes. Per client address: at most 10 attempts a
/// minute, whatever names are tried. Both are kept in memory.
/// </summary>
public sealed class SignInThrottle(TimeProvider time) : IDisposable
{
    public const int FreeFailures = 5;
    public const int AttemptsPerMinute = 10;
    public static readonly TimeSpan FirstLock = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan LongestLock = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Failures> _failures = new(StringComparer.Ordinal);

    private readonly PartitionedRateLimiter<string> _clients = PartitionedRateLimiter.Create<string, string>(
        client => RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = AttemptsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    /// <summary>Whether this client may make another attempt. Every call counts as one.</summary>
    public bool TryAcquireForClient(string clientAddress)
    {
        using var lease = _clients.AttemptAcquire(clientAddress);
        return lease.IsAcquired;
    }

    /// <summary>When the user name may be tried again, if it is locked now.</summary>
    public DateTimeOffset? LockedUntil(string userName)
    {
        var key = AccountRules.Normalize(userName);
        return _failures.TryGetValue(key, out var failures) && failures.LockedUntil > time.GetUtcNow()
            ? failures.LockedUntil
            : null;
    }

    public void RecordFailure(string userName)
    {
        var now = time.GetUtcNow();
        _failures.AddOrUpdate(
            AccountRules.Normalize(userName),
            _ => Lock(new Failures(1, DateTimeOffset.MinValue), now),
            (_, previous) => Lock(previous with { Count = previous.Count + 1 }, now));
    }

    public void RecordSuccess(string userName) => _failures.TryRemove(AccountRules.Normalize(userName), out _);

    public void Dispose() => _clients.Dispose();

    private static Failures Lock(Failures failures, DateTimeOffset now)
    {
        if (failures.Count < FreeFailures)
        {
            return failures;
        }
        var doublings = Math.Min(failures.Count - FreeFailures, 10);
        var length = TimeSpan.FromTicks(Math.Min(FirstLock.Ticks << doublings, LongestLock.Ticks));
        return failures with { LockedUntil = now + length };
    }

    private sealed record Failures(int Count, DateTimeOffset LockedUntil);
}
