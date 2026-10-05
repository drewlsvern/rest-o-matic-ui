using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Accounts.Setup;
using RestOMatic.Web.Features.Accounts.SignIn;
using RestOMatic.Web.Infrastructure.Storage;

namespace RestOMatic.Web.Tests;

/// <summary>
/// Starts the real app in memory against a data directory. With no argument
/// the directory is a fresh temporary one, deleted afterwards. Given a path,
/// the directory is the caller's to clean up, which lets a test start the app
/// twice on the same data.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly bool _ownsDataDirectory;
    private readonly Action<IWebHostBuilder>? _configure;

    /// <param name="configure">Extra settings or services for one test.</param>
    public AppFactory(string? dataDirectory = null, Action<IWebHostBuilder>? configure = null)
    {
        _ownsDataDirectory = dataDirectory is null;
        DataDirectory = dataDirectory ?? NewTemporaryPath();
        _configure = configure;
    }

    public string DataDirectory { get; }

    public string DatabaseFile => new DataDirectory(DataDirectory).DatabaseFile;

    public string KeysDirectory => new DataDirectory(DataDirectory).KeysDirectory;

    public static string NewTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"rest-o-matic-ui-tests-{Guid.NewGuid():N}");

    /// <summary>Meets every password rule.</summary>
    public const string Password = "Correct-horse7";

    /// <summary>A client that reports a redirect instead of following it.</summary>
    public HttpClient CreateDirectClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// A client with its own cookie jar, which reports redirects instead of
    /// following them. <paramref name="cookies"/> lets a test add or read cookies.
    /// </summary>
    public HttpClient CreateClientWithCookies(out CookieContainer cookies)
    {
        cookies = new CookieContainer();
        var client = CreateDefaultClient(new CookieContainerHandler(cookies));
        return client;
    }

    /// <summary>Creates a user directly, as setup or the users page would.</summary>
    public async Task<User> CreateUserAsync(string userName, string? email = null, string? displayName = null, string password = Password)
    {
        using var scope = Services.CreateScope();
        var (result, user) = await scope.ServiceProvider.GetRequiredService<UserAccounts>()
            .CreateAsync(new NewUser(userName, email ?? $"{userName}@example.com", displayName, password, password));
        Assert.True(result.Succeeded, string.Join(" ", result.Problems));
        Services.GetRequiredService<FirstRun>().Completed();
        return user!;
    }

    /// <summary>
    /// A client signed in as <paramref name="user"/> through the real cookie
    /// path: a ticket is issued as the sign-in page would, and redeemed over HTTP.
    /// </summary>
    public async Task<HttpClient> CreateSignedInClientAsync(User user)
    {
        var client = CreateClientWithCookies(out var cookies);
        const string binding = "test-browser-binding";
        cookies.Add(Server.BaseAddress, new Cookie(BrowserBinding.CookieName, binding));

        var ticket = Services.GetRequiredService<SignInTickets>().Issue(user.Id, "/", binding);
        using var response = await client.GetAsync(SignInHandler.CompletionUrl(ticket));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        return client;
    }

    /// <summary>Requests a page as a browser would, asking for HTML.</summary>
    public static Task<HttpResponseMessage> GetPageAsync(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return client.SendAsync(request);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(StorageRegistration.ConfigurationKey, DataDirectory);
        _configure?.Invoke(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Pooled connections keep the database file open after the app stops.
        SqliteConnection.ClearAllPools();
        if (disposing && _ownsDataDirectory && Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}
