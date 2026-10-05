using Microsoft.AspNetCore.DataProtection;

namespace RestOMatic.Web.Infrastructure.Storage;

public static class StorageRegistration
{
    public const string ConfigurationKey = "DataDirectory";

    public static void AddStorage(this IHostApplicationBuilder builder)
    {
        var dataDirectory = new DataDirectory(builder.Configuration[ConfigurationKey] ?? "data");
        builder.Services.AddSingleton(dataDirectory);

        // Kept in the data directory so that they survive a container
        // restart. Otherwise every restart would invalidate antiforgery
        // tokens and, once login exists, sign everyone out.
        builder.Services.AddDataProtection()
            .SetApplicationName("rest-o-matic-ui")
            .PersistKeysToFileSystem(new DirectoryInfo(dataDirectory.KeysDirectory));
    }

    /// <summary>Must run before anything touches the database.</summary>
    public static void InitialiseStorage(this WebApplication app)
    {
        var dataDirectory = app.Services.GetRequiredService<DataDirectory>();
        try
        {
            dataDirectory.EnsureWritable();
        }
        catch (InvalidOperationException ex)
        {
            app.Logger.LogCritical(ex, "Cannot start: {Message}", ex.Message);
            throw;
        }
    }
}
