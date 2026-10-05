using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Accounts.Setup;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Tests.Features.Accounts;

public partial class FirstRunTests
{
    [Fact]
    public async Task With_no_users_a_setup_token_is_logged_and_pages_lead_to_setup()
    {
        var logs = new LogRecorder();
        using var factory = AccountsTestSupport.Start(logs: logs);
        using var client = factory.CreateDirectClient();

        using var response = await AppFactory.GetPageAsync(client, "/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SetupPage.Path, response.Headers.Location!.OriginalString);
        var message = Assert.Single(logs.Messages, m => m.Contains("setup token"));
        Assert.Contains(SetupPage.Path, message);
        Assert.Matches(Token(), message);
    }

    [Fact]
    public async Task The_token_creates_the_first_user_once_and_signs_them_in()
    {
        var logs = new LogRecorder();
        using var factory = AccountsTestSupport.Start(logs: logs);
        factory.CreateDirectClient().Dispose();
        var token = LoggedToken(logs);

        var (result, completionUrl) = await SetUpAsync(factory, token);

        Assert.True(result.Succeeded, string.Join(" ", result.Problems));
        using var client = factory.CreateClientWithCookies(out var cookies);
        cookies.Add(factory.Server.BaseAddress, new Cookie(BrowserBinding.CookieName, "binding"));
        await client.GetAsync(completionUrl);
        Assert.True(await AccountsTestSupport.IsSignedInAsync(client));

        var (again, _) = await SetUpAsync(factory, token, userName: "mallory");
        Assert.False(again.Succeeded);
        Assert.Single(await factory.WithAccountsAsync(accounts => accounts.ListAsync()));
    }

    [Fact]
    public async Task A_wrong_token_is_refused()
    {
        using var factory = new AppFactory();
        factory.CreateDirectClient().Dispose();

        var (result, completionUrl) = await SetUpAsync(factory, "AAAAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.Equal([SetupHandler.WrongToken], result.Problems);
        Assert.Null(completionUrl);
        Assert.False(await factory.WithAccountsAsync(accounts => accounts.AnyAsync()));
    }

    [Fact]
    public async Task An_unacceptable_user_keeps_the_token_usable()
    {
        var logs = new LogRecorder();
        using var factory = AccountsTestSupport.Start(logs: logs);
        factory.CreateDirectClient().Dispose();
        var token = LoggedToken(logs);

        var (bad, _) = await SetUpAsync(factory, token, password: "short");
        var (good, _) = await SetUpAsync(factory, token);

        Assert.False(bad.Succeeded);
        Assert.True(good.Succeeded);
    }

    [Fact]
    public async Task Once_a_user_exists_setup_leads_to_sign_in_and_no_token_is_logged()
    {
        var dataDirectory = AppFactory.NewTemporaryPath();
        try
        {
            using (var first = new AppFactory(dataDirectory))
            {
                await first.CreateUserAsync("alice");
            }

            var logs = new LogRecorder();
            using var second = AccountsTestSupport.Start(logs: logs, dataDirectory: dataDirectory);
            using var client = second.CreateDirectClient();
            using var response = await AppFactory.GetPageAsync(client, SetupPage.Path);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(SignInEndpoints.SignInPath, response.Headers.Location!.OriginalString);
            Assert.DoesNotContain(logs.Messages, m => m.Contains("setup token"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task A_restart_before_setup_issues_a_new_token()
    {
        var dataDirectory = AppFactory.NewTemporaryPath();
        try
        {
            var firstLogs = new LogRecorder();
            string firstToken;
            using (var first = AccountsTestSupport.Start(logs: firstLogs, dataDirectory: dataDirectory))
            {
                first.CreateDirectClient().Dispose();
                firstToken = LoggedToken(firstLogs);
            }

            var secondLogs = new LogRecorder();
            using var second = AccountsTestSupport.Start(logs: secondLogs, dataDirectory: dataDirectory);
            second.CreateDirectClient().Dispose();

            Assert.NotEqual(firstToken, LoggedToken(secondLogs));
            var (result, _) = await SetUpAsync(second, firstToken);
            Assert.Equal([SetupHandler.WrongToken], result.Problems);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static async Task<(AccountResult Result, string? CompletionUrl)> SetUpAsync(
        AppFactory factory, string token, string userName = "alice", string password = AppFactory.Password)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SetupHandler>().CreateFirstUserAsync(
            new SetupInput(token, new NewUser(userName, $"{userName}@example.com", null, password, password)), "binding");
    }

    private static string LoggedToken(LogRecorder logs) =>
        Token().Match(Assert.Single(logs.Messages, m => m.Contains("setup token"))).Value;

    [GeneratedRegex(@"\b[A-Z2-7]{26}\b")]
    private static partial Regex Token();
}
