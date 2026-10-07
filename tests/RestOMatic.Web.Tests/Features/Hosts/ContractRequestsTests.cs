using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using RestOMatic.Web.Features.Hosts.Contract;

namespace RestOMatic.Web.Tests.Features.Hosts;

public sealed class ContractRequestsTests : IDisposable
{
    private readonly AppFactory _factory = new();

    [Fact]
    public async Task The_enrol_example_parses()
    {
        var message = await ReadAsync<EnrolRequest>(Contract.Example("enrol-request"));

        Assert.Equal("7GxK2mPq9sVw4tYb", message.Token);
        Assert.StartsWith("age1", message.PublicKey);
        Assert.Equal("prd-podman-01", message.Host.Hostname);
        Assert.Equal("0.17.3", message.Host.ResticVersion);
    }

    [Theory]
    [InlineData("checkin-quiet")]
    [InlineData("checkin-full")]
    [InlineData("checkin-config-withheld")]
    public async Task Every_check_in_example_parses(string example)
    {
        var message = await ReadAsync<CheckInRequest>(Contract.Example(example));

        Assert.Equal("8f2c6d1e-41b7-4c9a-9f0e-2b5d7a3c1e90", message.HostId);
        Assert.StartsWith("sha256:", message.Parts.Status.Fingerprint);
        Assert.StartsWith("sha256:", message.Parts.Config.Fingerprint);
    }

    [Fact]
    public async Task Part_content_keeps_fields_this_app_does_not_know()
    {
        var example = Contract.ExampleObject("checkin-full");
        example["parts"]!["status"]!["content"]!["something_new"] = "kept";

        var message = await ReadAsync<CheckInRequest>(example.ToJsonString());

        Assert.Contains("\"something_new\":\"kept\"", message.Parts.Status.Content!.Value.GetRawText());
    }

    [Fact]
    public async Task Unknown_top_level_fields_are_ignored()
    {
        var example = Contract.ExampleObject("checkin-quiet");
        example["added_in_a_later_version"] = 42;

        await ReadAsync<CheckInRequest>(example.ToJsonString());
    }

    [Fact]
    public async Task The_withheld_config_is_read()
    {
        var message = await ReadAsync<CheckInRequest>(Contract.Example("checkin-config-withheld"));

        Assert.Null(message.Parts.Config.Content);
        Assert.Equal("plain_text_secrets", message.Parts.Config.Withheld!.Reason);
        Assert.Equal(["repositories.offsite.password"], message.Parts.Config.Withheld.Fields);
    }

    [Fact]
    public async Task Another_format_version_is_unsupported_even_if_nothing_else_matches()
    {
        var (status, body) = await FailAsync<CheckInRequest>("""{"format_version": 2, "whatever": true}""");

        Assert.Equal(400, status);
        Assert.Equal(ContractErrors.UnsupportedFormat, Code(body));
    }

    [Theory]
    [InlineData("checkin-quiet", "parts")]
    [InlineData("checkin-quiet", "host")]
    [InlineData("checkin-quiet", "sent_at")]
    [InlineData("enrol-request", "token")]
    [InlineData("enrol-request", "public_key")]
    public async Task A_missing_field_is_an_invalid_request_that_names_it(string example, string field)
    {
        var message = Contract.ExampleObject(example);
        message.Remove(field);

        var (status, body) = example.StartsWith("enrol")
            ? await FailAsync<EnrolRequest>(message.ToJsonString())
            : await FailAsync<CheckInRequest>(message.ToJsonString());

        Assert.Equal(400, status);
        Assert.Equal(ContractErrors.InvalidRequest, Code(body));
        Assert.Contains(field, Message(body));
    }

    [Fact]
    public async Task A_missing_nested_field_is_named()
    {
        var message = Contract.ExampleObject("checkin-quiet");
        message["parts"]!.AsObject().Remove("snapshots");

        var (_, body) = await FailAsync<CheckInRequest>(message.ToJsonString());

        Assert.Equal(ContractErrors.InvalidRequest, Code(body));
        Assert.Contains("snapshots", Message(body));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{}")]
    public async Task Something_that_is_not_a_message_is_an_invalid_request(string text)
    {
        var (status, body) = await FailAsync<CheckInRequest>(text);

        Assert.Equal(400, status);
        Assert.Equal(ContractErrors.InvalidRequest, Code(body));
    }

    [Theory]
    [InlineData(400, ContractErrors.InvalidRequest)]
    [InlineData(400, ContractErrors.UnsupportedFormat)]
    [InlineData(401, ContractErrors.TokenRejected)]
    [InlineData(401, ContractErrors.CredentialRejected)]
    [InlineData(413, ContractErrors.RequestTooLarge)]
    [InlineData(429, ContractErrors.RateLimited)]
    public async Task Every_error_body_matches_the_schema(int status, string code)
    {
        var body = await ExecuteAsync(ContractErrors.Reply(status, code, "Something went wrong."));

        Contract.AssertMatches("error", body.Body);
        Assert.Equal(status, body.Status);
    }

    private async Task<T> ReadAsync<T>(string json) where T : class
    {
        var read = await ContractRequests.ReadAsync<T>(Request(json), maxBytes: null, CancellationToken.None);
        if (read.Error is not null)
        {
            Assert.Fail((await ExecuteAsync(read.Error)).Body);
        }
        return read.Message!;
    }

    private async Task<(int Status, string Body)> FailAsync<T>(string json) where T : class
    {
        var read = await ContractRequests.ReadAsync<T>(Request(json), maxBytes: null, CancellationToken.None);
        Assert.NotNull(read.Error);
        var reply = await ExecuteAsync(read.Error);
        Contract.AssertMatches("error", reply.Body);
        return reply;
    }

    private static HttpRequest Request(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return context.Request;
    }

    private async Task<(int Status, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext { RequestServices = _factory.Services };
        var stream = new MemoryStream();
        context.Response.Body = stream;
        await result.ExecuteAsync(context);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string Code(string body) => JsonNode.Parse(body)!["error"]!["code"]!.GetValue<string>();

    private static string Message(string body) => JsonNode.Parse(body)!["error"]!["message"]!.GetValue<string>();

    public void Dispose() => _factory.Dispose();
}
