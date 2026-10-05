using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace RestOMatic.Web.Features.Accounts.SignIn;

/// <summary>
/// A sign-in page runs over the circuit and cannot set a cookie. Once it has
/// checked the password it gets a ticket here, and the browser carries the
/// ticket to <c>/account/complete</c>, an ordinary request that can.
/// A ticket works once, for 30 seconds, and only in the browser it was made
/// for: it records a hash of that browser's binding cookie.
/// </summary>
public sealed class SignInTickets(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);

    public string Issue(Guid userId, string returnUrl, string browserBinding)
    {
        var now = time.GetUtcNow();
        foreach (var (key, old) in _tickets)
        {
            if (old.ExpiresAt <= now)
            {
                _tickets.TryRemove(key, out _);
            }
        }

        var value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _tickets[value] = new Ticket(userId, ReturnUrls.Local(returnUrl), Hash(browserBinding), now + Lifetime);
        return value;
    }

    /// <summary>
    /// The ticket's user and return address, if it is current and presented
    /// by the browser it was made for. Any attempt spends the ticket.
    /// </summary>
    public (Guid UserId, string ReturnUrl)? Redeem(string? value, string? browserBinding)
    {
        if (value is null || !_tickets.TryRemove(value, out var ticket))
        {
            return null;
        }
        if (ticket.ExpiresAt <= time.GetUtcNow()
            || browserBinding is null
            || !CryptographicOperations.FixedTimeEquals(ticket.BindingHash, Hash(browserBinding)))
        {
            return null;
        }
        return (ticket.UserId, ticket.ReturnUrl);
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private sealed record Ticket(Guid UserId, string ReturnUrl, byte[] BindingHash, DateTimeOffset ExpiresAt);
}

public static class ReturnUrls
{
    /// <summary>The address if it is a path on this site, otherwise <c>/</c>.</summary>
    public static string Local(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return "/";
        }
        // "//host" and "/\host" are read by browsers as another site.
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
        {
            return "/";
        }
        return url.Any(char.IsControl) ? "/" : url;
    }
}
