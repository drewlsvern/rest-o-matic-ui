using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Features.Health;

public static class HealthFeature
{
    public const string Path = "/healthz";

    public static void AddHealth(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>();
    }

    /// <summary>
    /// 200 when the app is running and its database can be opened, 503 when
    /// the database cannot. Mapped as a plain endpoint, outside the Blazor
    /// components, so it can be left open when login is added.
    /// </summary>
    public static void MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(Path);
    }
}
