namespace RestOMatic.Web.Tests.Features.Hosts;

/// <summary>The contract copy is present and its own examples match its schemas.</summary>
public class ContractTests
{
    [Theory]
    [InlineData("enrol-request", "enrol-request")]
    [InlineData("enrol-response", "enrol-response")]
    [InlineData("checkin-quiet", "checkin-request")]
    [InlineData("checkin-full", "checkin-request")]
    [InlineData("checkin-config-withheld", "checkin-request")]
    [InlineData("checkin-response", "checkin-response")]
    [InlineData("checkin-response-resend", "checkin-response")]
    [InlineData("error-credential-rejected", "error")]
    [InlineData("error-token-rejected", "error")]
    public void Every_example_matches_its_schema(string example, string schema)
    {
        Contract.AssertMatches(schema, Contract.Example(example));
    }

    [Fact]
    public void Every_example_is_covered_above()
    {
        Assert.Equal(9, Contract.ExampleNames().Count());
    }

    [Fact]
    public void The_validator_rejects_what_the_schema_forbids()
    {
        var reply = Contract.ExampleObject("checkin-response");
        reply.Remove("resend");

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Contract.AssertMatches("checkin-response", reply.ToJsonString()));
    }
}
