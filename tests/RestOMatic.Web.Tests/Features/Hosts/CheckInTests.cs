using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestOMatic.Web.Features.Hosts;
using RestOMatic.Web.Features.Hosts.Contract;
using RestOMatic.Web.Tests.Features.Accounts;

namespace RestOMatic.Web.Tests.Features.Hosts;

public class CheckInTests
{
    [Fact]
    public async Task A_full_check_in_keeps_every_part_and_replies_as_the_contract_says()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();
        var request = HostsTestSupport.CheckIn("checkin-full", hostId);

        var (status, body) = await factory.PostCheckInAsync(credential, request.ToJsonString());

        Assert.Equal(200, status);
        Contract.AssertMatches("checkin-response", body);
        var reply = JsonNode.Parse(body)!;
        Assert.Empty(reply["resend"]!.AsArray());
        Assert.Null(reply["config"]);
        Assert.Empty(reply["actions"]!.AsArray());
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", reply["server_time"]!.GetValue<string>());

        var host = await factory.LoadHostAsync(hostId);
        var parts = request["parts"]!;
        foreach (var kind in PartKinds.All)
        {
            var part = Assert.Single(host.Parts, p => p.Kind == kind);
            Assert.Equal(parts[kind]!["fingerprint"]!.GetValue<string>(), part.Fingerprint);
            Assert.True(part.IsCurrent);
        }
        Assert.Equal(parts["config"]!["content"]!.GetValue<string>(), host.Parts.Single(p => p.Kind == "config").Content);
        Assert.True(JsonNode.DeepEquals(parts["status"]!["content"], JsonNode.Parse(host.Parts.Single(p => p.Kind == "status").Content!)));
        Assert.Equal(parts["status"]!["content"]!["jobs"]!.AsArray().Count, host.JobCount);
        Assert.NotNull(host.LastCheckInAt);
    }

    [Fact]
    public async Task A_quiet_check_in_after_a_full_one_needs_nothing_resent()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();
        await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-full", hostId).ToJsonString());

        var (status, body) = await factory.PostCheckInAsync(credential, Quiet(hostId, after: "checkin-full").ToJsonString());

        Assert.Equal(200, status);
        Contract.AssertMatches("checkin-response", body);
        Assert.Empty(HostsTestSupport.Resend(body));
    }

    [Fact]
    public async Task Each_check_in_records_the_time_and_the_hosts_description()
    {
        var clock = AccountsTestSupport.NewClock();
        using var factory = AccountsTestSupport.Start(clock);
        var (hostId, credential) = await factory.EnrolledHostAsync();
        var request = HostsTestSupport.CheckIn("checkin-quiet", hostId);
        request["host"]!["rest_o_matic_version"] = "v0.2.1";

        clock.Advance(TimeSpan.FromMinutes(5));
        await factory.PostCheckInAsync(credential, request.ToJsonString());

        var host = await factory.LoadHostAsync(hostId);
        Assert.Equal("v0.2.1", host.RestOMaticVersion);
        Assert.Equal(clock.GetUtcNow(), host.LastCheckInAt);
    }

    [Fact]
    public async Task Parts_the_app_has_no_copy_of_are_asked_for()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();

        var (_, body) = await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-quiet", hostId).ToJsonString());

        Assert.Equal(["status", "snapshots", "config"], HostsTestSupport.Resend(body));
        Contract.AssertMatches("checkin-response", body);
    }

    [Fact]
    public async Task A_part_whose_copy_is_out_of_date_is_asked_for()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();
        await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-full", hostId).ToJsonString());
        var quiet = Quiet(hostId, after: "checkin-full");
        quiet["parts"]!["snapshots"]!["fingerprint"] = "sha256:" + new string('0', 64);

        var (_, body) = await factory.PostCheckInAsync(credential, quiet.ToJsonString());

        Assert.Equal(["snapshots"], HostsTestSupport.Resend(body));
    }

    [Fact]
    public async Task A_withheld_config_is_recorded_and_not_asked_for()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();

        var (status, body) = await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-config-withheld", hostId).ToJsonString());

        Assert.Equal(200, status);
        Assert.DoesNotContain("config", HostsTestSupport.Resend(body));
        var config = (await factory.LoadHostAsync(hostId)).Parts.Single(p => p.Kind == "config");
        Assert.False(config.IsCurrent);
        Assert.Equal("plain_text_secrets", config.WithheldReason);
        Assert.Equal("""["repositories.offsite.password"]""", config.WithheldFields);
    }

    [Fact]
    public async Task A_withheld_config_keeps_the_last_text_until_one_is_sent_again()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();
        var full = HostsTestSupport.CheckIn("checkin-full", hostId);
        full["sent_at"] = "2026-10-03T09:00:00Z";
        await factory.PostCheckInAsync(credential, full.ToJsonString());
        var text = full["parts"]!["config"]!["content"]!.GetValue<string>();

        var withheld = HostsTestSupport.CheckIn("checkin-config-withheld", hostId);
        withheld["sent_at"] = "2026-10-03T09:05:00Z";
        await factory.PostCheckInAsync(credential, withheld.ToJsonString());
        var whileWithheld = (await factory.LoadHostAsync(hostId)).Parts.Single(p => p.Kind == "config");
        Assert.Equal(text, whileWithheld.Content);
        Assert.False(whileWithheld.IsCurrent);

        var again = HostsTestSupport.CheckIn("checkin-full", hostId);
        again["sent_at"] = "2026-10-03T09:10:00Z";
        again["parts"]!["config"]!["content"] = "jobs: {}\n";
        await factory.PostCheckInAsync(credential, again.ToJsonString());
        var current = (await factory.LoadHostAsync(hostId)).Parts.Single(p => p.Kind == "config");
        Assert.Equal("jobs: {}\n", current.Content);
        Assert.True(current.IsCurrent);
        Assert.Null(current.WithheldReason);
        Assert.Null(current.WithheldFields);
    }

    [Fact]
    public async Task Of_two_overlapping_check_ins_the_later_sent_at_wins()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();
        var later = HostsTestSupport.CheckIn("checkin-full", hostId);
        later["sent_at"] = "2026-10-03T09:02:00Z";
        later["parts"]!["status"]!["fingerprint"] = "sha256:later";
        var earlier = HostsTestSupport.CheckIn("checkin-full", hostId);
        earlier["sent_at"] = "2026-10-03T09:01:00Z";
        earlier["parts"]!["status"]!["fingerprint"] = "sha256:earlier";

        await factory.PostCheckInAsync(credential, later.ToJsonString());
        var (status, body) = await factory.PostCheckInAsync(credential, earlier.ToJsonString());

        Assert.Equal(200, status);
        Assert.Equal("sha256:later", (await factory.LoadHostAsync(hostId)).Parts.Single(p => p.Kind == "status").Fingerprint);
        // The app holds something newer than the late arrival; asking again would only repeat it.
        Assert.Empty(HostsTestSupport.Resend(body));
    }

    [Fact]
    public async Task A_gzip_compressed_check_in_is_accepted()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();

        var (status, _) = await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-full", hostId).ToJsonString(), gzip: true);

        Assert.Equal(200, status);
        Assert.Equal(3, (await factory.LoadHostAsync(hostId)).Parts.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("rom1_not-a-credential-of-any-host")]
    public async Task Without_a_valid_credential_the_credential_is_rejected(string? credential)
    {
        using var factory = new AppFactory();
        var (hostId, _) = await factory.EnrolledHostAsync();

        var (status, body) = await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-quiet", hostId).ToJsonString());

        AssertCredentialRejected(status, body);
    }

    [Fact]
    public async Task A_removed_hosts_credential_is_rejected()
    {
        using var factory = new AppFactory();
        var (hostId, credential) = await factory.EnrolledHostAsync();
        using (var scope = factory.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<HostRegistry>().RemoveAsync(hostId));
        }

        var (status, body) = await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-quiet", hostId).ToJsonString());

        AssertCredentialRejected(status, body);
    }

    [Fact]
    public async Task Another_hosts_id_is_rejected_and_nothing_is_stored()
    {
        using var factory = new AppFactory();
        var (_, credential) = await factory.EnrolledHostAsync("first");
        var (otherId, _) = await factory.EnrolledHostAsync("second");

        var (status, body) = await factory.PostCheckInAsync(credential, HostsTestSupport.CheckIn("checkin-full", otherId).ToJsonString());

        AssertCredentialRejected(status, body);
        Assert.Empty((await factory.LoadHostAsync(otherId)).Parts);
    }

    [Fact]
    public async Task A_session_cookie_does_not_send_a_check_in()
    {
        using var factory = new AppFactory();
        var (hostId, _) = await factory.EnrolledHostAsync();
        using var signedIn = await factory.CreateSignedInClientAsync(await factory.CreateUserAsync("alice"));

        using var response = await signedIn.PostAsync(
            "/api/v1/checkin",
            new StringContent(HostsTestSupport.CheckIn("checkin-quiet", hostId).ToJsonString(), System.Text.Encoding.UTF8, "application/json"));

        AssertCredentialRejected((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_host_credential_opens_no_page()
    {
        using var factory = new AppFactory();
        await factory.CreateUserAsync("alice");
        var (_, credential) = await factory.EnrolledHostAsync();
        using var client = factory.CreateDirectClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/hosts");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/sign-in", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_check_in_over_the_limit_is_refused_and_logged_with_the_host()
    {
        var logs = new LogRecorder();
        using var factory = new AppFactory(configure: builder =>
        {
            builder.UseSetting("CheckIn:MaxRequestBytes", "2048");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        var (hostId, credential) = await factory.EnrolledHostAsync("big-host");
        var full = HostsTestSupport.CheckIn("checkin-full", hostId).ToJsonString();
        Assert.True(full.Length > 2048);

        foreach (var gzip in new[] { false, true })
        {
            var (status, body) = await factory.PostCheckInAsync(credential, full, gzip);

            Assert.Equal(413, status);
            Contract.AssertMatches("error", body);
            Assert.Equal(ContractErrors.RequestTooLarge, HostsTestSupport.ErrorCode(body));
        }
        Assert.Empty((await factory.LoadHostAsync(hostId)).Parts);
        Assert.Contains(logs.Messages, m => m.Contains("big-host") && m.Contains("2048") && m.Contains("CheckIn:MaxRequestBytes"));
    }

    [Fact]
    public async Task A_raised_limit_accepts_a_larger_check_in()
    {
        using var factory = new AppFactory(configure: builder => builder.UseSetting("CheckIn:MaxRequestBytes", "67108864"));
        var (hostId, credential) = await factory.EnrolledHostAsync();
        var full = HostsTestSupport.CheckIn("checkin-full", hostId);
        // About 40 MiB of status content: a field the contract lets the app ignore and keep.
        full["parts"]!["status"]!["content"]!["padding"] = new string('x', 40 * 1024 * 1024);

        var (status, _) = await factory.PostCheckInAsync(credential, full.ToJsonString(), gzip: true);

        Assert.Equal(200, status);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("lots")]
    public void An_invalid_limit_stops_the_app_from_starting(string value)
    {
        using var factory = new AppFactory(configure: builder => builder.UseSetting("CheckIn:MaxRequestBytes", value));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("MaxRequestBytes", error.ToString());
    }

    /// <summary>The quiet example, carrying the fingerprints another example sent.</summary>
    private static JsonObject Quiet(Guid hostId, string after)
    {
        var quiet = HostsTestSupport.CheckIn("checkin-quiet", hostId);
        var parts = Contract.ExampleObject(after)["parts"]!;
        foreach (var kind in PartKinds.All)
        {
            quiet["parts"]![kind]!["fingerprint"] = parts[kind]!["fingerprint"]!.GetValue<string>();
        }
        quiet["sent_at"] = "2026-10-03T10:00:00Z";
        return quiet;
    }

    private static void AssertCredentialRejected(int status, string body)
    {
        Assert.Equal(401, status);
        Contract.AssertMatches("error", body);
        Assert.Equal(ContractErrors.CredentialRejected, HostsTestSupport.ErrorCode(body));
    }
}
