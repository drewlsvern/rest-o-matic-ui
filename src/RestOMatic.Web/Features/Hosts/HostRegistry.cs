using Microsoft.EntityFrameworkCore;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Features.Hosts;

/// <summary>What the hosts page shows about a host. Never includes a secret.</summary>
public sealed record HostSummary(
    Guid Id,
    string Name,
    bool IsEnrolled,
    DateTimeOffset? EnrolTokenExpiresAt,
    string? Hostname,
    string? Os,
    string? Arch,
    string? RestOMaticVersion,
    string? ResticVersion,
    DateTimeOffset? LastCheckInAt,
    int? JobCount);

/// <summary>A newly issued enrol token, shown once.</summary>
public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>What a signed-in user does with hosts: add, list, issue a new enrol token, remove.</summary>
public sealed class HostRegistry(AppDbContext db, TimeProvider time)
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    public Task<List<HostSummary>> ListAsync() =>
        db.Set<Host>().AsNoTracking()
            .OrderBy(h => h.NormalizedName)
            .Select(h => new HostSummary(
                h.Id, h.Name, h.CredentialHash != null, h.EnrolTokenExpiresAt,
                h.Hostname, h.Os, h.Arch, h.RestOMaticVersion, h.ResticVersion, h.LastCheckInAt, h.JobCount))
            .ToListAsync();

    public async Task<(IReadOnlyList<string> Problems, Guid? HostId, IssuedToken? Token)> AddAsync(string? name)
    {
        var problems = HostNames.Check(name);
        if (problems.Count > 0)
        {
            return (problems, null, null);
        }
        var normalized = HostNames.Normalize(name!);
        if (await db.Set<Host>().AnyAsync(h => h.NormalizedName == normalized))
        {
            return (["That host name is already taken."], null, null);
        }

        var host = new Host { Name = name!.Trim(), CreatedAt = time.GetUtcNow() };
        var token = Issue(host);
        db.Add(host);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return (["That host name is already taken."], null, null);
        }
        return ([], host.Id, token);
    }

    /// <summary>A new enrol token for a host; any earlier unused one stops working.</summary>
    public async Task<IssuedToken?> NewTokenAsync(Guid hostId)
    {
        var host = await db.Set<Host>().FirstOrDefaultAsync(h => h.Id == hostId);
        if (host is null)
        {
            return null;
        }
        var token = Issue(host);
        await db.SaveChangesAsync();
        return token;
    }

    /// <summary>Removes the host and what it reported; its credential and token stop working.</summary>
    public async Task<bool> RemoveAsync(Guid hostId)
    {
        var host = await db.Set<Host>().FirstOrDefaultAsync(h => h.Id == hostId);
        if (host is null)
        {
            return false;
        }
        db.Remove(host);
        await db.SaveChangesAsync();
        return true;
    }

    private IssuedToken Issue(Host host)
    {
        var token = Secrets.NewEnrolToken();
        var expiresAt = time.GetUtcNow() + TokenLifetime;
        host.SetEnrolToken(Secrets.HashEnrolToken(token), expiresAt);
        return new IssuedToken(token, expiresAt);
    }
}

/// <summary>
/// A circuit lives as long as its tab, so pages take a fresh registry, with
/// its own database context, for each operation.
/// </summary>
public static class HostsScope
{
    public static async Task<T> WithHostsAsync<T>(this IServiceScopeFactory scopes, Func<HostRegistry, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<HostRegistry>());
    }
}
