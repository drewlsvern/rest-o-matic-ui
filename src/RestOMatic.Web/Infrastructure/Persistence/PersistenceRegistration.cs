using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestOMatic.Web.Infrastructure.Storage;

namespace RestOMatic.Web.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static void AddPersistence(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<AppDbContext>((services, options) =>
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = services.GetRequiredService<DataDirectory>().DatabaseFile,
            };
            options.UseSqlite(connectionString.ToString());
        });
    }

    /// <summary>
    /// Brings the database up to date, creating it on first start. One
    /// process owns one database file, so there is no separate migration step.
    /// </summary>
    public static void MigrateDatabase(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }
}
