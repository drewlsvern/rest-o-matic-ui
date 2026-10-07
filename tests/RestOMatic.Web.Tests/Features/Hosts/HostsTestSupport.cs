using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestOMatic.Web.Features.Hosts;
using RestOMatic.Web.Features.Hosts.CheckIn;
using RestOMatic.Web.Features.Hosts.Enrolment;
using RestOMatic.Web.Infrastructure.Persistence;

namespace RestOMatic.Web.Tests.Features.Hosts;

/// <summary>Talking to the host endpoints as a host would.</summary>
internal static class HostsTestSupport
{
    public static async Task<(Guid HostId, string Token)> AddHostAsync(this AppFactory factory, string name = "prd-podman-01")
    {
        using var scope = factory.Services.CreateScope();
        var (problems, hostId, token) = await scope.ServiceProvider.GetRequiredService<HostRegistry>().AddAsync(name);
        Assert.Empty(problems);
        return (hostId!.Value, token!.Token);
    }

    public static async Task<string> NewTokenAsync(this AppFactory factory, Guid hostId)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<HostRegistry>().NewTokenAsync(hostId))!.Token;
    }

    /// <summary>The contract's enrol example with the given token, and optionally another hostname.</summary>
    public static Task<HttpResponseMessage> PostEnrolAsync(HttpClient client, string token, string? hostname = null)
    {
        var request = Contract.ExampleObject("enrol-request");
        request["token"] = token;
        if (hostname is not null)
        {
            request["host"]!["hostname"] = hostname;
        }
        return client.PostAsync(EnrolEndpoint.Path, Json(request.ToJsonString()));
    }

    /// <summary>Enrols and returns the credential from the reply.</summary>
    public static async Task<string> EnrolAsync(this AppFactory factory, string token, string? hostname = null)
    {
        using var client = factory.CreateDirectClient();
        using var response = await PostEnrolAsync(client, token, hostname);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonNode.Parse(body)!["credential"]!.GetValue<string>();
    }

    /// <summary>Adds and enrols a host, returning its ID and credential.</summary>
    public static async Task<(Guid HostId, string Credential)> EnrolledHostAsync(this AppFactory factory, string name = "prd-podman-01")
    {
        var (hostId, token) = await factory.AddHostAsync(name);
        return (hostId, await factory.EnrolAsync(token));
    }

    /// <summary>A check-in body: the named example, with this host's ID.</summary>
    public static JsonObject CheckIn(string example, Guid hostId)
    {
        var message = Contract.ExampleObject(example);
        message["host_id"] = hostId.ToString();
        return message;
    }

    public static async Task<(int Status, string Body)> PostCheckInAsync(
        this AppFactory factory, string? credential, string json, bool gzip = false)
    {
        using var client = factory.CreateDirectClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, CheckInEndpoint.Path)
        {
            Content = gzip ? Gzip(json) : Json(json),
        };
        if (credential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public static async Task<Host> LoadHostAsync(this AppFactory factory, Guid hostId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Host>()
            .AsNoTracking().Include(h => h.Parts).SingleAsync(h => h.Id == hostId);
    }

    public static string? ErrorCode(string body) =>
        JsonNode.Parse(body)?["error"]?["code"]?.GetValue<string>();

    public static IReadOnlyList<string> Resend(string body) =>
        JsonNode.Parse(body)!["resend"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static ByteArrayContent Gzip(string json)
    {
        using var buffer = new MemoryStream();
        using (var zip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zip.Write(Encoding.UTF8.GetBytes(json));
        }
        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }
}
