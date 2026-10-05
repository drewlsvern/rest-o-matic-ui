using System.Diagnostics;
using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Tests.Features.Accounts;

public class ResetPasswordCommandTests
{
    [Fact]
    public async Task Resets_a_user_and_prints_a_password_that_works_and_meets_the_rules()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        using var client = await factory.CreateSignedInClientAsync(alice);
        var output = new StringWriter();

        var exitCode = await ResetPasswordCommand.RunAsync(factory.Services, ["alice"], output, new StringWriter());

        Assert.Equal(0, exitCode);
        var password = output.ToString().Split('\n').Select(line => line.Trim()).Single(line => line.Length == 20);
        Assert.Empty(AccountRules.CheckPassword(password, "alice"));
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("alice", password));
        Assert.IsType<SignInOutcome.Refused>(await factory.SignInAsync("alice", AppFactory.Password, client: "10.0.0.2"));
        Assert.False(await AccountsTestSupport.IsSignedInAsync(client));
    }

    [Fact]
    public async Task An_unknown_user_fails_and_changes_nothing()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        var error = new StringWriter();

        var exitCode = await ResetPasswordCommand.RunAsync(factory.Services, ["nobody"], new StringWriter(), error);

        Assert.Equal(1, exitCode);
        Assert.Contains("nobody", error.ToString());
        Assert.IsType<SignInOutcome.Succeeded>(await factory.SignInAsync("alice", AppFactory.Password));
    }

    [Fact]
    public async Task Without_a_user_name_it_shows_how_to_use_it()
    {
        using var factory = new AppFactory();
        var error = new StringWriter();

        var exitCode = await ResetPasswordCommand.RunAsync(factory.Services, [], new StringWriter(), error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage: reset-password <user name>", error.ToString());
    }

    [Fact]
    public async Task The_app_runs_the_command_and_exits_without_starting_the_web_server()
    {
        var dataDirectory = AppFactory.NewTemporaryPath();
        try
        {
            var start = new ProcessStartInfo("dotnet", [typeof(Program).Assembly.Location, ResetPasswordCommand.Name, "nobody"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment = { ["DataDirectory"] = dataDirectory, ["ASPNETCORE_URLS"] = "http://127.0.0.1:0" },
            };
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(1, process.ExitCode);
            Assert.Contains("There is no user named 'nobody'.", await error);
            Assert.DoesNotContain("Now listening", await output);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }
}
