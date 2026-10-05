using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Features.Accounts.Sessions;

/// <summary>
/// A circuit keeps the user it started with, and makes no HTTP requests that
/// would let the cookie handler notice a session has ended. This provider
/// makes an open page notice: at once when its session is ended in this
/// process (sign-out in another tab, a password reset, removal), and within
/// a minute when the user's stamp is changed elsewhere (the reset command).
/// </summary>
public sealed class SessionAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly IServiceScopeFactory _scopes;
    private readonly SessionStore _store;
    private string? _sessionId;

    public SessionAuthenticationStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopes, SessionStore store)
        : base(loggerFactory)
    {
        _scopes = scopes;
        _store = store;
        _store.SessionEnded += OnSessionEnded;
        AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        var sessionId = AccountClaims.SessionId(state.User);
        var userId = AccountClaims.UserId(state.User);
        if (sessionId is null || userId is null || !_store.IsActive(sessionId))
        {
            return false;
        }

        // The session's own stamp, which an own-password change keeps up to date.
        var stamp = _store.StampOf(sessionId);
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<User>()
            .AnyAsync(u => u.Id == userId && u.SecurityStamp == stamp, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.SessionEnded -= OnSessionEnded;
            AuthenticationStateChanged -= OnAuthenticationStateChanged;
        }
        base.Dispose(disposing);
    }

    private async void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        try
        {
            _sessionId = AccountClaims.SessionId((await task).User);
        }
        catch (Exception)
        {
            _sessionId = null;
        }
    }

    private void OnSessionEnded(string sessionId)
    {
        if (sessionId == _sessionId)
        {
            _sessionId = null;
            SetAuthenticationState(Task.FromResult(Anonymous));
        }
    }
}
