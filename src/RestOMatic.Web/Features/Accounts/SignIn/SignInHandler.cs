namespace RestOMatic.Web.Features.Accounts.SignIn;

public abstract record SignInOutcome
{
    /// <summary>The password was right; the browser should go to <see cref="CompletionUrl"/>.</summary>
    public sealed record Succeeded(string CompletionUrl) : SignInOutcome;

    public sealed record Refused(string Message) : SignInOutcome;
}

/// <summary>Checks a user name and password and, if they are right, issues the ticket that sets the cookie.</summary>
public sealed class SignInHandler(
    UserAccounts accounts,
    Passwords passwords,
    SignInThrottle throttle,
    SignInTickets tickets,
    TimeProvider time)
{
    public const string WrongCredentials = "Incorrect user name or password.";
    public const string TooManyFromClient = "Too many sign-in attempts from your address. Try again in a minute.";

    public async Task<SignInOutcome> SignInAsync(
        string? userName, string? password, string clientAddress, string browserBinding, string? returnUrl)
    {
        if (!throttle.TryAcquireForClient(clientAddress))
        {
            return new SignInOutcome.Refused(TooManyFromClient);
        }
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return new SignInOutcome.Refused(WrongCredentials);
        }

        userName = userName.Trim();
        if (throttle.LockedUntil(userName) is { } lockedUntil)
        {
            return new SignInOutcome.Refused(Locked(lockedUntil));
        }

        var user = await accounts.FindForSignInAsync(userName);
        if (!passwords.Verify(user, password, out var rehashed) || user is null)
        {
            throttle.RecordFailure(userName);
            return throttle.LockedUntil(userName) is { } nowLocked
                ? new SignInOutcome.Refused($"{WrongCredentials} {Locked(nowLocked)}")
                : new SignInOutcome.Refused(WrongCredentials);
        }

        throttle.RecordSuccess(userName);
        if (rehashed is not null)
        {
            await accounts.UpgradeHashAsync(user, rehashed);
        }
        return new SignInOutcome.Succeeded(CompletionUrl(tickets.Issue(user.Id, ReturnUrls.Local(returnUrl), browserBinding)));
    }

    public static string CompletionUrl(string ticket) =>
        $"{SignInEndpoints.CompletePath}?ticket={Uri.EscapeDataString(ticket)}";

    private string Locked(DateTimeOffset until)
    {
        var wait = until - time.GetUtcNow();
        var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        var text = seconds < 120
            ? (seconds == 1 ? "1 second" : $"{seconds} seconds")
            : $"{(int)Math.Ceiling(wait.TotalMinutes)} minutes";
        return $"Too many failed attempts for this user. Try again in {text}.";
    }
}
