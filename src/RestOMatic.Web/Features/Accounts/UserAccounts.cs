using Microsoft.EntityFrameworkCore;
using RestOMatic.Web.Features.Accounts.Profile;
using RestOMatic.Web.Features.Accounts.Sessions;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Features.Accounts;

/// <summary>The outcome of a change to an account: either it worked, or every reason it did not.</summary>
public sealed record AccountResult(IReadOnlyList<string> Problems)
{
    public static AccountResult Ok { get; } = new([]);

    public bool Succeeded => Problems.Count == 0;

    public static AccountResult Fail(params IEnumerable<string> problems) => new([.. problems]);
}

public sealed record NewUser(string UserName, string Email, string? DisplayName, string Password, string Confirmation);

/// <summary>What the UI shows about a user. Never includes the password hash or the picture's bytes.</summary>
public sealed record UserSummary(
    Guid Id, string UserName, string Email, string? DisplayName, bool HasPicture, int PictureVersion)
{
    public string ShownName => string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName;
}

/// <summary>
/// Every change to users and their passwords. Setup, the users page, the
/// profile dialog and the reset command all go through here, so the rules
/// and the ending of sessions are applied the same way everywhere.
/// </summary>
public sealed class UserAccounts(
    AppDbContext db,
    Passwords passwords,
    SessionStore sessions,
    AccountEvents events,
    TimeProvider time)
{
    public Task<bool> AnyAsync() => db.Set<User>().AnyAsync();

    public Task<List<UserSummary>> ListAsync() =>
        db.Set<User>().AsNoTracking().OrderBy(u => u.NormalizedUserName).Select(ToSummary).ToListAsync();

    public Task<UserSummary?> FindAsync(Guid id) =>
        db.Set<User>().AsNoTracking().Where(u => u.Id == id).Select(ToSummary).FirstOrDefaultAsync();

    /// <summary>The user and their password, for checking a sign-in. Matched on user name only, never email.</summary>
    public Task<User?> FindForSignInAsync(string userName)
    {
        var normalized = AccountRules.Normalize(userName);
        return db.Set<User>().Include(u => u.Password).FirstOrDefaultAsync(u => u.NormalizedUserName == normalized);
    }

    public Task<User?> FindUserAsync(Guid id) => db.Set<User>().FirstOrDefaultAsync(u => u.Id == id);

    public async Task<(AccountResult Result, User? User)> CreateAsync(NewUser input)
    {
        List<string> problems =
        [
            .. AccountRules.CheckUserName(input.UserName),
            .. AccountRules.CheckEmail(input.Email),
            .. AccountRules.CheckDisplayName(input.DisplayName),
            .. AccountRules.CheckNewPassword(input.Password, input.Confirmation, input.UserName),
        ];
        if (problems.Count == 0)
        {
            problems.AddRange(await TakenAsync(input.UserName, input.Email, except: null));
        }
        if (problems.Count > 0)
        {
            return (AccountResult.Fail(problems), null);
        }

        var now = time.GetUtcNow();
        var user = new User
        {
            UserName = input.UserName.Trim(),
            Email = input.Email.Trim(),
            DisplayName = Tidy(input.DisplayName),
            CreatedAt = now,
        };
        user.Password = new PasswordCredential { UserId = user.Id, Hash = passwords.Hash(user, input.Password), ChangedAt = now };
        db.Add(user);
        if (!await TrySaveAsync())
        {
            return (AccountResult.Fail("That user name or email address is already in use."), null);
        }
        return (AccountResult.Ok, user);
    }

    /// <summary>Display name and email. Ends no session.</summary>
    public async Task<AccountResult> UpdateProfileAsync(Guid userId, string? displayName, string email)
    {
        var user = await FindUserAsync(userId);
        if (user is null)
        {
            return AccountResult.Fail("That user no longer exists.");
        }
        List<string> problems = [.. AccountRules.CheckEmail(email), .. AccountRules.CheckDisplayName(displayName)];
        if (problems.Count == 0)
        {
            problems.AddRange(await TakenAsync(userName: null, email, except: userId));
        }
        if (problems.Count > 0)
        {
            return AccountResult.Fail(problems);
        }

        user.Email = email.Trim();
        user.DisplayName = Tidy(displayName);
        if (!await TrySaveAsync())
        {
            return AccountResult.Fail("That email address is already in use.");
        }
        events.RaiseProfileChanged(userId);
        return AccountResult.Ok;
    }

    /// <summary>Another user sets this user's password. All of this user's sessions end.</summary>
    public async Task<AccountResult> SetPasswordAsync(Guid userId, string password, string confirmation)
    {
        var user = await db.Set<User>().Include(u => u.Password).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return AccountResult.Fail("That user no longer exists.");
        }
        var problems = AccountRules.CheckNewPassword(password, confirmation, user.UserName);
        if (problems.Count > 0)
        {
            return AccountResult.Fail(problems);
        }

        await ReplacePasswordAsync(user, password);
        sessions.EndSessionsOf(userId);
        return AccountResult.Ok;
    }

    /// <summary>
    /// A user changes their own password. Their other sessions end; the one
    /// they changed it from (<paramref name="currentSessionId"/>) stays signed in.
    /// </summary>
    public async Task<AccountResult> ChangeOwnPasswordAsync(
        Guid userId, string currentPassword, string password, string confirmation, string? currentSessionId)
    {
        var user = await db.Set<User>().Include(u => u.Password).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return AccountResult.Fail("That user no longer exists.");
        }
        if (!passwords.Verify(user, currentPassword, out _))
        {
            return AccountResult.Fail("The current password is incorrect.");
        }
        var problems = AccountRules.CheckNewPassword(password, confirmation, user.UserName);
        if (problems.Count > 0)
        {
            return AccountResult.Fail(problems);
        }

        await ReplacePasswordAsync(user, password);
        sessions.EndSessionsOf(userId, except: currentSessionId);
        if (currentSessionId is not null)
        {
            sessions.UpdateStamp(currentSessionId, user.SecurityStamp);
        }
        return AccountResult.Ok;
    }

    /// <summary>For the reset command: a new generated password, which is returned to be shown once.</summary>
    public async Task<(AccountResult Result, string? Password)> ResetPasswordAsync(string userName)
    {
        var user = await FindForSignInAsync(userName);
        if (user is null)
        {
            return (AccountResult.Fail($"There is no user named '{userName}'."), null);
        }
        var password = Passwords.Generate();
        await ReplacePasswordAsync(user, password);
        sessions.EndSessionsOf(user.Id);
        return (AccountResult.Ok, password);
    }

    public async Task<AccountResult> RemoveAsync(Guid actingUserId, Guid userId)
    {
        if (actingUserId == userId)
        {
            return AccountResult.Fail("You cannot remove your own account.");
        }
        var user = await FindUserAsync(userId);
        if (user is null)
        {
            return AccountResult.Fail("That user no longer exists.");
        }
        if (await db.Set<User>().CountAsync() <= 1)
        {
            return AccountResult.Fail("The last user cannot be removed.");
        }

        db.Remove(user);
        await db.SaveChangesAsync();
        sessions.EndSessionsOf(userId);
        return AccountResult.Ok;
    }

    public async Task<AccountResult> SetPictureAsync(Guid userId, Stream upload)
    {
        var (problem, contentType, data) = await ProfilePictures.ReadAsync(upload);
        if (problem is not null)
        {
            return AccountResult.Fail(problem);
        }
        var user = await db.Set<User>().Include(u => u.Picture).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return AccountResult.Fail("That user no longer exists.");
        }

        user.SetPicture(contentType!, data!);
        await db.SaveChangesAsync();
        events.RaiseProfileChanged(userId);
        return AccountResult.Ok;
    }

    public async Task<AccountResult> RemovePictureAsync(Guid userId)
    {
        var user = await db.Set<User>().Include(u => u.Picture).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return AccountResult.Fail("That user no longer exists.");
        }
        if (user.HasPicture)
        {
            user.RemovePicture();
            await db.SaveChangesAsync();
            events.RaiseProfileChanged(userId);
        }
        return AccountResult.Ok;
    }

    public Task<ProfilePicture?> FindPictureAsync(Guid userId) =>
        db.Set<ProfilePicture>().AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);

    /// <summary>Saves a newer hash after a successful sign-in with a password in an older format.</summary>
    public async Task UpgradeHashAsync(User user, string hash)
    {
        if (user.Password is not null)
        {
            user.Password.Hash = hash;
            await db.SaveChangesAsync();
        }
    }

    private async Task ReplacePasswordAsync(User user, string password)
    {
        var now = time.GetUtcNow();
        if (user.Password is null)
        {
            user.Password = new PasswordCredential { UserId = user.Id, Hash = passwords.Hash(user, password), ChangedAt = now };
        }
        else
        {
            user.Password.Hash = passwords.Hash(user, password);
            user.Password.ChangedAt = now;
        }
        user.ChangeSecurityStamp();
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> TakenAsync(string? userName, string email, Guid? except)
    {
        List<string> problems = [];
        if (userName is not null)
        {
            var name = AccountRules.Normalize(userName);
            if (await db.Set<User>().AnyAsync(u => u.NormalizedUserName == name && u.Id != except))
            {
                problems.Add("That user name is already taken.");
            }
        }
        var normalizedEmail = AccountRules.Normalize(email);
        if (await db.Set<User>().AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.Id != except))
        {
            problems.Add("That email address is already in use.");
        }
        return problems;
    }

    /// <summary>The unique indexes are the final word when two changes race.</summary>
    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static readonly System.Linq.Expressions.Expression<Func<User, UserSummary>> ToSummary =
        u => new UserSummary(u.Id, u.UserName, u.Email, u.DisplayName, u.HasPicture, u.PictureVersion);

    private static string? Tidy(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
}

/// <summary>Lets open pages, such as the profile menu, refresh when a user's profile changes.</summary>
public sealed class AccountEvents
{
    public event Action<Guid>? ProfileChanged;

    public void RaiseProfileChanged(Guid userId) => ProfileChanged?.Invoke(userId);
}

/// <summary>
/// A circuit lives for as long as its tab is open, so a page that uses
/// <see cref="UserAccounts"/> takes a fresh one, with its own database
/// context, for each operation.
/// </summary>
public static class AccountsScope
{
    public static async Task<T> WithAccountsAsync<T>(this IServiceScopeFactory scopes, Func<UserAccounts, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<UserAccounts>());
    }
}
