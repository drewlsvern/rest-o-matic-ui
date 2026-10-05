using System.Security.Cryptography;
using System.Text;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Features.Accounts.Setup;

/// <summary>
/// The state of a new install. While no user exists, a one-time setup token
/// is written to the log at startup, and every page leads to the setup page,
/// where the token creates the first user.
/// </summary>
public sealed class FirstRun(ILogger<FirstRun> logger)
{
    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private readonly Lock _lock = new();
    private volatile bool _hasUsers = true;
    private string? _token;

    public bool HasUsers => _hasUsers;

    /// <summary>At startup: if nobody can sign in yet, issue a token and log it.</summary>
    public void Start(bool hasUsers)
    {
        var token = hasUsers ? null : new string(RandomNumberGenerator.GetItems<char>(Base32, 26));
        lock (_lock)
        {
            _hasUsers = hasUsers;
            _token = token;
        }
        if (token is not null)
        {
            logger.LogWarning(
                "No user exists yet. Open {SetupPath} and enter this setup token to create the first user: {SetupToken}",
                SetupPage.Path, token);
        }
    }

    /// <summary>
    /// Takes the token if it is the right one, so no one else can use it
    /// while the first user is being created. <see cref="Return"/> gives it
    /// back if creating the user then fails.
    /// </summary>
    public bool TryClaim(string? token)
    {
        lock (_lock)
        {
            if (_hasUsers || _token is null || token is null
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_token), Encoding.UTF8.GetBytes(token.Trim().ToUpperInvariant())))
            {
                return false;
            }
            _token = null;
            return true;
        }
    }

    public void Return(string token)
    {
        lock (_lock)
        {
            if (!_hasUsers)
            {
                _token = token.Trim().ToUpperInvariant();
            }
        }
    }

    public void Completed()
    {
        lock (_lock)
        {
            _hasUsers = true;
            _token = null;
        }
    }

    /// <summary>
    /// Middleware: while no user exists, pages lead to setup; once one does,
    /// setup leads to sign-in.
    /// </summary>
    public async Task RedirectAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        var isPage = HttpMethods.IsGet(request.Method)
            && request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
        var isSetup = request.Path.Equals(SetupPage.Path, StringComparison.OrdinalIgnoreCase);

        if (isPage && !HasUsers && !isSetup)
        {
            context.Response.Redirect(SetupPage.Path);
            return;
        }
        if (isPage && HasUsers && isSetup)
        {
            context.Response.Redirect(SignInEndpoints.SignInPath);
            return;
        }
        await next(context);
    }
}

public sealed record SetupInput(string Token, NewUser User);

/// <summary>Creates the first user with the setup token, then signs them in like any other sign-in.</summary>
public sealed class SetupHandler(FirstRun firstRun, UserAccounts accounts, SignInTickets tickets)
{
    public const string WrongToken = "That setup token is not correct. Check the app's log for the current one.";

    public async Task<(AccountResult Result, string? CompletionUrl)> CreateFirstUserAsync(SetupInput input, string browserBinding)
    {
        if (firstRun.HasUsers)
        {
            return (AccountResult.Fail("Setup is already complete. Sign in instead."), null);
        }
        if (!firstRun.TryClaim(input.Token))
        {
            return (AccountResult.Fail(WrongToken), null);
        }

        var (result, user) = await accounts.CreateAsync(input.User);
        if (!result.Succeeded || user is null)
        {
            firstRun.Return(input.Token);
            return (result, null);
        }

        firstRun.Completed();
        return (AccountResult.Ok, SignInHandler.CompletionUrl(tickets.Issue(user.Id, "/", browserBinding)));
    }
}
