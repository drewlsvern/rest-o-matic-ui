using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace RestOMatic.Web.Tests.Features.Hosts;

/// <summary>
/// The host contract copied from rest-o-matic (contract/checkin/v1): its
/// examples, and validation of this app's messages against its schemas.
/// </summary>
internal static class Contract
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "contract", "checkin", "v1");

    /// <summary>An example's text, by file name without ".json".</summary>
    public static string Example(string name) => File.ReadAllText(Path.Combine(Folder, "examples", name + ".json"));

    /// <summary>An example as a mutable JSON object, for tests that change one field.</summary>
    public static JsonObject ExampleObject(string name) => JsonNode.Parse(Example(name))!.AsObject();

    public static IEnumerable<string> ExampleNames() =>
        Directory.GetFiles(Path.Combine(Folder, "examples"), "*.json").Select(Path.GetFileNameWithoutExtension)!;

    /// <summary>
    /// Fails the test with every reason when <paramref name="json"/> does not
    /// match the schema, by file name without ".schema.json".
    /// </summary>
    public static void AssertMatches(string schema, string json)
    {
        var result = Schema(schema).Evaluate(JsonDocument.Parse(json).RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });
        var problems = (result.Details ?? [])
            .Where(detail => detail.Errors is { Count: > 0 })
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}"));
        Assert.True(result.IsValid, $"Does not match {schema}.schema.json:\n{string.Join("\n", problems)}\n{json}");
    }

    // Every schema is loaded once, up front: each has an $id that may be
    // registered only once, and they refer to each other by it (the check-in
    // request uses the enrol request's host description).
    private static readonly Dictionary<string, JsonSchema> Schemas = Directory
        .GetFiles(Folder, "*.schema.json")
        .ToDictionary(path => Path.GetFileName(path)[..^".schema.json".Length], path => JsonSchema.FromFile(path));

    private static JsonSchema Schema(string name) => Schemas[name];
}
