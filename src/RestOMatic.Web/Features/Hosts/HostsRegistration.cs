using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using RestOMatic.Web.Features.Hosts.CheckIn;
using RestOMatic.Web.Features.Hosts.Contract;
using RestOMatic.Web.Features.Hosts.Enrolment;

namespace RestOMatic.Web.Features.Hosts;

/// <summary>Settings for the host endpoints and the enrol command.</summary>
public sealed class HostsOptions
{
    /// <summary>
    /// The address hosts use to reach this app, written into the enrol
    /// command. When unset, the address the user's browser used is shown.
    /// </summary>
    [Url]
    public string? PublicUrl { get; set; }
}

public sealed class CheckInOptions
{
    public const string Section = "CheckIn";

    /// <summary>
    /// The largest check-in accepted, after decompression. 32 MiB holds about
    /// 70,000 snapshots; raise it for a host with more.
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "CheckIn:MaxRequestBytes must be a positive whole number of bytes.")]
    public long MaxRequestBytes { get; set; } = 32 * 1024 * 1024;
}

public static class HostsRegistration
{
    public const int EnrolAttemptsPerMinute = 10;

    public static void AddHosts(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<HostsOptions>().BindConfiguration("").ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CheckInOptions>().BindConfiguration(CheckInOptions.Section).ValidateDataAnnotations().ValidateOnStart();

        services.AddScoped<HostRegistry>();
        services.AddScoped<EnrolHandler>();
        services.AddScoped<CheckInHandler>();
        services.AddSingleton<CheckInLocks>();

        // Hosts authenticate with their credential, never with the sign-in
        // cookie; the policy names only this scheme.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, HostCredentialAuthenticationHandler>(HostCredentialAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(HostCredentialAuthenticationHandler.Policy, policy => policy
                .AddAuthenticationSchemes(HostCredentialAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());

        services.AddRequestDecompression();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(EnrolEndpoint.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = EnrolAttemptsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            options.OnRejected = (context, _) => new ValueTask(ContractErrors.WriteAsync(
                context.HttpContext.Response, StatusCodes.Status429TooManyRequests, ContractErrors.RateLimited,
                "Too many enrol attempts from this address. Try again in a minute."));
        });
    }

    /// <summary>Before anything reads a request body.</summary>
    public static void UseHosts(this WebApplication app)
    {
        // gzip bodies from hosts, unwrapped within each endpoint's size limit.
        app.UseRequestDecompression();
        app.UseRateLimiter();
    }

    public static void MapHosts(this IEndpointRouteBuilder endpoints)
    {
        var maxRequestBytes = endpoints.ServiceProvider.GetRequiredService<IOptions<CheckInOptions>>().Value.MaxRequestBytes;
        endpoints.MapEnrol();
        endpoints.MapCheckIn(maxRequestBytes);
    }
}
