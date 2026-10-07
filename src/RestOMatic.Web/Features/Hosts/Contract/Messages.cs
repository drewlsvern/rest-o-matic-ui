using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestOMatic.Web.Features.Hosts.Contract;

// The messages of contract/checkin/v1 (rest-o-matic owns it; see
// contract/PROVENANCE.md). Members the schema requires are `required`, so a
// message missing one is refused. Fields this app does not know are ignored,
// as the contract asks, and part content is kept as the host sent it.

public sealed record EnrolRequest
{
    public required int FormatVersion { get; init; }

    public required string Token { get; init; }

    public required string PublicKey { get; init; }

    public required HostMessage Host { get; init; }
}

public sealed record HostMessage
{
    public required string Hostname { get; init; }

    public required string Os { get; init; }

    public required string Arch { get; init; }

    public required string RestOMaticVersion { get; init; }

    public required string? ResticVersion { get; init; }

    public HostDescription ToDescription() => new(Hostname, Os, Arch, RestOMaticVersion, ResticVersion);
}

public sealed record EnrolResponse(
    int FormatVersion, string HostId, string HostName, string Credential, IReadOnlyList<string> RecoveryRecipients);

public sealed record CheckInRequest
{
    public required int FormatVersion { get; init; }

    public required string? HostId { get; init; }

    public required DateTimeOffset SentAt { get; init; }

    public required HostMessage Host { get; init; }

    public required CheckInParts Parts { get; init; }
}

public sealed record CheckInParts
{
    public required JsonPart Status { get; init; }

    public required JsonPart Snapshots { get; init; }

    public required ConfigPart Config { get; init; }
}

/// <summary><c>status</c> or <c>snapshots</c>: content is null unless the part changed.</summary>
public sealed record JsonPart
{
    public required string Fingerprint { get; init; }

    public required JsonElement? Content { get; init; }
}

public sealed record ConfigPart
{
    public required string Fingerprint { get; init; }

    public required string? Content { get; init; }

    public required Withheld? Withheld { get; init; }
}

public sealed record Withheld
{
    public required string Reason { get; init; }

    public required IReadOnlyList<string> Fields { get; init; }
}

public sealed record CheckInResponse(
    int FormatVersion,
    string ServerTime,
    IReadOnlyList<string> Resend,
    object? Config,
    IReadOnlyList<object> Actions);

public sealed record ErrorBody(ErrorDetail Error);

public sealed record ErrorDetail(string Code, string Message);

public static class ContractJson
{
    public const int FormatVersion = 1;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>RFC 3339 in UTC, to the second, as the contract writes times.</summary>
    public static string Time(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
