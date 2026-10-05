namespace RestOMatic.Web.Features.Accounts;

/// <summary>
/// <c>reset-password &lt;user name&gt;</c>: for when nobody can sign in. Run
/// with the app's data directory, for example inside the container with
/// <c>podman exec rest-o-matic-ui dotnet RestOMatic.Web.dll reset-password alice</c>.
/// It sets a generated password, prints it once and exits without starting
/// the web server. A running app ends that user's sessions at their next
/// request, because their security stamp has changed.
/// </summary>
public static class ResetPasswordCommand
{
    public const string Name = "reset-password";

    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        if (arguments.Count != 1 || string.IsNullOrWhiteSpace(arguments[0]))
        {
            await error.WriteLineAsync($"Usage: {Name} <user name>");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var (result, password) = await scope.ServiceProvider.GetRequiredService<UserAccounts>().ResetPasswordAsync(arguments[0]);
        if (!result.Succeeded)
        {
            foreach (var problem in result.Problems)
            {
                await error.WriteLineAsync(problem);
            }
            return 1;
        }

        await output.WriteLineAsync($"The password for '{arguments[0]}' is now:");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"    {password}");
        await output.WriteLineAsync();
        await output.WriteLineAsync("It is shown only this once. Sign in with it, then change it from the profile menu.");
        return 0;
    }
}
