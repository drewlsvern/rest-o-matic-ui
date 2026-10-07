using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using RestOMatic.Web.Features.Hosts;
using RestOMatic.Web.Features.Hosts.Contract;
using RestOMatic.Web.Tests.Features.Accounts;

namespace RestOMatic.Web.Tests.Features.Hosts;

public class EnrolTests
{
    [Fact]
    public async Task Enrolling_replies_as_the_contract_says_with_the_given_name()
    {
        using var factory = new AppFactory();
        var (hostId, token) = await factory.AddHostAsync("backup-box");
        using var client = factory.CreateDirectClient();

        using var response = await HostsTestSupport.PostEnrolAsync(client, token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Contract.AssertMatches("enrol-response", body);
        var reply = JsonNode.Parse(body)!;
        Assert.Equal(hostId.ToString(), reply["host_id"]!.GetValue<string>());
        Assert.Equal("backup-box", reply["host_name"]!.GetValue<string>());
        Assert.StartsWith("rom1_", reply["credential"]!.GetValue<string>());
        Assert.Empty(reply["recovery_recipients"]!.AsArray());
    }

    [Fact]
    public async Task Enrolling_records_the_key_and_description()
    {
        using var factory = new AppFactory();
        var (hostId, token) = await factory.AddHostAsync();

        await factory.EnrolAsync(token);

        var host = await factory.LoadHostAsync(hostId);
        Assert.True(host.IsEnrolled);
        Assert.StartsWith("age1ql3z7hjy54pw3", host.PublicKey);
        Assert.Equal("prd-podman-01", host.Hostname);
        Assert.Equal(("linux", "amd64", "v0.2.0", "0.17.3"), (host.Os, host.Arch, host.RestOMaticVersion, host.ResticVersion));
        Assert.Null(host.EnrolTokenHash);
    }

    [Fact]
    public async Task A_token_works_once()
    {
        using var factory = new AppFactory();
        var (_, token) = await factory.AddHostAsync();
        await factory.EnrolAsync(token);

        await AssertTokenRejectedAsync(factory, token);
    }

    [Fact]
    public async Task A_token_expires_after_24_hours()
    {
        var clock = AccountsTestSupport.NewClock();
        using var factory = AccountsTestSupport.Start(clock);
        var (hostId, token) = await factory.AddHostAsync();

        clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        await AssertTokenRejectedAsync(factory, token);
        Assert.False((await factory.LoadHostAsync(hostId)).IsEnrolled);
    }

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        using var factory = new AppFactory();
        await factory.AddHostAsync();

        await AssertTokenRejectedAsync(factory, Secrets.NewEnrolToken());
    }

    [Fact]
    public async Task A_new_command_replaces_the_old_token()
    {
        using var factory = new AppFactory();
        var (hostId, oldToken) = await factory.AddHostAsync();
        var newToken = await factory.NewTokenAsync(hostId);

        await AssertTokenRejectedAsync(factory, oldToken);
        await factory.EnrolAsync(newToken);
    }

    [Fact]
    public async Task The_token_is_accepted_in_any_case()
    {
        using var factory = new AppFactory();
        var (_, token) = await factory.AddHostAsync();

        await factory.EnrolAsync(token.ToLowerInvariant());
    }

    [Fact]
    public async Task Enrolling_again_replaces_the_credential_and_keeps_what_was_reported()
    {
        using var factory = new AppFactory();
        var (hostId, oldCredential) = await factory.EnrolledHostAsync();
        await factory.PostCheckInAsync(oldCredential, HostsTestSupport.CheckIn("checkin-full", hostId).ToJsonString());

        var newCredential = await factory.EnrolAsync(await factory.NewTokenAsync(hostId));

        Assert.NotEqual(oldCredential, newCredential);
        var quiet = HostsTestSupport.CheckIn("checkin-quiet", hostId).ToJsonString();
        Assert.Equal(401, (await factory.PostCheckInAsync(oldCredential, quiet)).Status);
        var (status, body) = await factory.PostCheckInAsync(newCredential, quiet);
        Assert.Equal(200, status);
        Assert.Empty(HostsTestSupport.Resend(body));
        Assert.Equal(3, (await factory.LoadHostAsync(hostId)).Parts.Count);
    }

    [Fact]
    public async Task The_eleventh_attempt_in_a_minute_is_refused()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateDirectClient();

        for (var i = 0; i < HostsRegistration.EnrolAttemptsPerMinute; i++)
        {
            using var attempt = await HostsTestSupport.PostEnrolAsync(client, Secrets.NewEnrolToken());
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }
        using var response = await HostsTestSupport.PostEnrolAsync(client, Secrets.NewEnrolToken());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Contract.AssertMatches("error", body);
        Assert.Equal(ContractErrors.RateLimited, HostsTestSupport.ErrorCode(body));
    }

    [Fact]
    public async Task A_host_name_is_unique_whatever_the_case()
    {
        using var factory = new AppFactory();
        await factory.AddHostAsync("prd-podman-01");
        using var scope = factory.Services.CreateScope();

        var (problems, _, token) = await scope.ServiceProvider.GetRequiredService<HostRegistry>().AddAsync("PRD-podman-01");

        Assert.Equal(["That host name is already taken."], problems);
        Assert.Null(token);
    }

    private static async Task AssertTokenRejectedAsync(AppFactory factory, string token)
    {
        using var client = factory.CreateDirectClient();
        using var response = await HostsTestSupport.PostEnrolAsync(client, token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Contract.AssertMatches("error", body);
        Assert.Equal(ContractErrors.TokenRejected, HostsTestSupport.ErrorCode(body));
    }
}
