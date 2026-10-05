using System.Net;
using Microsoft.Data.Sqlite;

namespace RestOMatic.Web.Tests.Infrastructure.Storage;

public class DataDirectoryTests
{
    [Fact]
    public async Task First_start_creates_the_directory_and_the_database()
    {
        using var factory = new AppFactory();
        Assert.False(Directory.Exists(factory.DataDirectory));
        using var client = factory.CreateDirectClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(factory.DatabaseFile));
    }

    [Fact]
    public void Start_fails_and_names_the_directory_when_it_cannot_be_written()
    {
        // A directory cannot be created beneath a regular file, whoever runs
        // the tests, so this does not depend on file permissions.
        var file = Path.GetTempFileName();
        try
        {
            var dataDirectory = Path.Combine(file, "data");
            using var factory = new AppFactory(dataDirectory);

            var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateDirectClient());

            Assert.Contains(dataDirectory, exception.Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Restart_keeps_the_database_and_the_keys()
    {
        var dataDirectory = AppFactory.NewTemporaryPath();
        try
        {
            string[] keysBefore;
            using (var first = new AppFactory(dataDirectory))
            {
                using var client = first.CreateDirectClient();
                // Rendering a page makes the app create a data-protection key.
                await client.GetStringAsync("/");
                keysBefore = KeyFiles(first);
                Assert.NotEmpty(keysBefore);
                Execute(first.DatabaseFile, "CREATE TABLE restart_marker (id INTEGER)");
            }

            using (var second = new AppFactory(dataDirectory))
            {
                using var client = second.CreateDirectClient();
                await client.GetStringAsync("/");

                Assert.Equal(keysBefore, KeyFiles(second));
                // Throws if the database was recreated and the table is gone.
                Execute(second.DatabaseFile, "SELECT id FROM restart_marker");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static string[] KeyFiles(AppFactory factory) =>
        Directory.GetFiles(factory.KeysDirectory).Order().ToArray();

    private static void Execute(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databaseFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
