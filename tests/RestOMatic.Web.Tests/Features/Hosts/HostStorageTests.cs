using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RestOMatic.Web.Features.Hosts;
using RestOMatic.Web.Infrastructure.Persistence;
using RestOMatic.Web.Infrastructure.Storage;

namespace RestOMatic.Web.Tests.Features.Hosts;

public class HostStorageTests
{
    [Theory]
    [InlineData("prd-podman-01", true)]
    [InlineData("db.example_2", true)]
    [InlineData("élodie-nas", true)]
    [InlineData("", false)]
    [InlineData("two words", false)]
    [InlineData("slash/name", false)]
    public void Host_name_characters(string name, bool acceptable)
    {
        Assert.Equal(acceptable, HostNames.Check(name).Count == 0);
    }

    [Fact]
    public void Host_name_length()
    {
        Assert.Empty(HostNames.Check(new string('a', 64)));
        Assert.NotEmpty(HostNames.Check(new string('a', 65)));
    }

    [Fact]
    public void An_enrol_token_is_26_base32_characters_and_matches_in_any_case()
    {
        var token = Secrets.NewEnrolToken();

        Assert.Matches("^[A-Z2-7]{26}$", token);
        Assert.Equal(Secrets.HashEnrolToken(token), Secrets.HashEnrolToken(" " + token.ToLowerInvariant() + " "));
        Assert.NotEqual(token, Secrets.NewEnrolToken());
    }

    [Fact]
    public void A_credential_has_its_prefix_and_256_random_bits()
    {
        var credential = Secrets.NewCredential();

        Assert.StartsWith("rom1_", credential);
        Assert.Matches("^rom1_[A-Za-z0-9_-]{43}$", credential);
        Assert.NotEqual(credential, Secrets.NewCredential());
    }

    [Fact]
    public async Task Only_hashes_of_the_secrets_are_stored()
    {
        using var factory = new AppFactory();
        var token = Secrets.NewEnrolToken();
        var credential = Secrets.NewCredential();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var host = new Host { Name = "prd-podman-01" };
            host.SetEnrolToken(Secrets.HashEnrolToken(token), DateTimeOffset.UtcNow.AddHours(1));
            db.Add(host);
            await db.SaveChangesAsync();
            host.Enrol(Secrets.HashCredential(credential), "age1abc", new HostDescription("h", "linux", "amd64", "v0.2.0", null), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        SqliteConnection.ClearAllPools();

        var bytes = await File.ReadAllBytesAsync(factory.DatabaseFile);
        var text = System.Text.Encoding.Latin1.GetString(bytes)
            + System.Text.Encoding.Latin1.GetString(File.Exists(factory.DatabaseFile + "-wal") ? await File.ReadAllBytesAsync(factory.DatabaseFile + "-wal") : []);
        Assert.Contains("prd-podman-01", text);
        Assert.DoesNotContain(credential, text);
        Assert.DoesNotContain(credential[5..], text);
        Assert.DoesNotContain(token, text);
    }

    [Fact]
    public async Task A_database_from_the_previous_change_is_upgraded_and_keeps_its_users()
    {
        var dataDirectory = AppFactory.NewTemporaryPath();
        try
        {
            // The database as the password-sign-in change left it.
            Directory.CreateDirectory(dataDirectory);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={new DataDirectory(dataDirectory).DatabaseFile}")
                .Options;
            await using (var db = new AppDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync("AddAccounts");
                Assert.DoesNotContain("AddHosts", string.Join(",", await db.Database.GetAppliedMigrationsAsync()));
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO Users (Id, UserName, NormalizedUserName, Email, NormalizedEmail, SecurityStamp, CreatedAt, PictureVersion, HasPicture)
                    VALUES ({0}, 'alice', 'ALICE', 'alice@example.com', 'ALICE@EXAMPLE.COM', {1}, {2}, 0, 0)
                    """,
                    Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
            }
            SqliteConnection.ClearAllPools();

            using var factory = new AppFactory(dataDirectory);
            using var scope = factory.Services.CreateScope();
            var upgraded = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.Contains(await upgraded.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_AddHosts"));
            Assert.Equal(0, await upgraded.Set<Host>().CountAsync());
            Assert.True(await upgraded.Set<RestOMatic.Web.Features.Accounts.User>().AnyAsync(u => u.UserName == "alice"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
