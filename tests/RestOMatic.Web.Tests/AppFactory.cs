using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
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

    public AppFactory(string? dataDirectory = null)
    {
        _ownsDataDirectory = dataDirectory is null;
        DataDirectory = dataDirectory ?? NewTemporaryPath();
    }

    public string DataDirectory { get; }

    public string DatabaseFile => new DataDirectory(DataDirectory).DatabaseFile;

    public string KeysDirectory => new DataDirectory(DataDirectory).KeysDirectory;

    public static string NewTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"rest-o-matic-ui-tests-{Guid.NewGuid():N}");

    /// <summary>A client that reports a redirect instead of following it.</summary>
    public HttpClient CreateDirectClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(StorageRegistration.ConfigurationKey, DataDirectory);
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
