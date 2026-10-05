using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Accounts.Sessions;
using RestOMatic.Web.Features.Accounts.SignIn;

namespace RestOMatic.Web.Tests.Features.Accounts;

/// <summary>Ways to start the app for the accounts tests, and to look inside its sessions.</summary>
internal static class AccountsTestSupport
{
    /// <summary>
    /// A clock that starts now: cookies carry expiry dates from this clock,
    /// and the test client's cookie jar judges them by the real one.
    /// </summary>
    public static FakeTimeProvider NewClock() => new(DateTimeOffset.UtcNow);

    public static AppFactory Start(
        FakeTimeProvider? clock = null,
        LogRecorder? logs = null,
        string? dataDirectory = null,
        string? environment = null) =>
        new(dataDirectory, builder =>
        {
            if (environment is not null)
            {
                builder.UseEnvironment(environment);
                // Outside Development the app expects a published layout. The
                // test runs from the build output, so package assets
                // (MudBlazor's) are found through the static web assets manifest.
                builder.UseStaticWebAssets();
            }
            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }
            if (clock is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                });
            }
        });

    public static async Task<T> WithAccountsAsync<T>(this AppFactory factory, Func<UserAccounts, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<UserAccounts>());
    }

    public static async Task<SignInOutcome> SignInAsync(
        this AppFactory factory, string userName, string password, string client = "10.0.0.1", string binding = "binding")
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SignInHandler>()
            .SignInAsync(userName, password, client, binding, returnUrl: null);
    }

    /// <summary>Whether the client's session is still accepted: a page loads rather than sending it to sign in.</summary>
    public static async Task<bool> IsSignedInAsync(HttpClient client)
    {
        using var response = await AppFactory.GetPageAsync(client, "/");
        return response.StatusCode switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.Redirect when response.Headers.Location!.OriginalString.Contains(SignInEndpoints.SignInPath) => false,
            var status => throw new InvalidOperationException($"Unexpected {status} from /."),
        };
    }

    /// <summary>The session cookie as the app reads it, decrypted.</summary>
    public static AuthenticationTicket? ReadSessionCookie(AppFactory factory, CookieContainer cookies)
    {
        var value = cookies.GetCookies(factory.Server.BaseAddress)[AccountSessions.CookieName]?.Value;
        if (value is null)
        {
            return null;
        }
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        return options.TicketDataFormat.Unprotect(value);
    }

    /// <summary>The key of the client's session in the session store.</summary>
    public static string SessionIdOf(AppFactory factory, CookieContainer cookies) =>
        ReadSessionCookie(factory, cookies)!.Principal.Claims
            .Single(claim => claim.Type == "Microsoft.AspNetCore.Authentication.Cookies-SessionId").Value;

    public static async Task<(HttpClient Client, CookieContainer Cookies)> SignedInWithCookiesAsync(this AppFactory factory, User user)
    {
        var client = factory.CreateClientWithCookies(out var cookies);
        const string binding = "test-browser-binding";
        cookies.Add(factory.Server.BaseAddress, new Cookie(BrowserBinding.CookieName, binding));
        var ticket = factory.Services.GetRequiredService<SignInTickets>().Issue(user.Id, "/", binding);
        using var response = await client.GetAsync(SignInHandler.CompletionUrl(ticket));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (client, cookies);
    }

    public static HttpRequestMessage NotAPage(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }
}

/// <summary>Keeps every log message the app writes, for tests that read the log.</summary>
internal sealed class LogRecorder : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new Logger(_messages);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception));
    }
}
