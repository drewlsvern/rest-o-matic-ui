namespace RestOMatic.Web.Features.Hosts;

/// <summary>
/// A server running rest-o-matic. It is added by a user, then enrols with a
/// one-time token and checks in with its credential. Its <see cref="Id"/> is
/// the contract's <c>host_id</c>.
/// </summary>
public sealed class Host
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name
    {
        get;
        init
        {
            field = value;
            NormalizedName = HostNames.Normalize(value);
        }
    }

    public string NormalizedName { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The hash of the one unused enrol token, if there is one.</summary>
    public byte[]? EnrolTokenHash { get; private set; }

    public DateTimeOffset? EnrolTokenExpiresAt { get; private set; }

    /// <summary>The hash of the credential the host checks in with. Null until it enrols.</summary>
    public byte[]? CredentialHash { get; private set; }

    /// <summary>The host's age public key, for locking secrets for it later.</summary>
    public string? PublicKey { get; private set; }

    public DateTimeOffset? EnrolledAt { get; private set; }

    public string? Hostname { get; private set; }

    public string? Os { get; private set; }

    public string? Arch { get; private set; }

    public string? RestOMaticVersion { get; private set; }

    public string? ResticVersion { get; private set; }

    public DateTimeOffset? LastCheckInAt { get; private set; }

    /// <summary>How many jobs the latest status reported.</summary>
    public int? JobCount { get; set; }

    public List<HostPart> Parts { get; init; } = [];

    public bool IsEnrolled => CredentialHash is not null;

    /// <summary>A new token replaces any earlier unused one.</summary>
    public void SetEnrolToken(byte[] hash, DateTimeOffset expiresAt)
    {
        EnrolTokenHash = hash;
        EnrolTokenExpiresAt = expiresAt;
    }

    /// <summary>
    /// Spends the token and replaces the credential and key, so a host that
    /// enrols again stops using its old credential. Its reports are kept.
    /// </summary>
    public void Enrol(byte[] credentialHash, string publicKey, HostDescription description, DateTimeOffset now)
    {
        EnrolTokenHash = null;
        EnrolTokenExpiresAt = null;
        CredentialHash = credentialHash;
        PublicKey = publicKey;
        EnrolledAt = now;
        Describe(description);
    }

    public void CheckedIn(HostDescription description, DateTimeOffset now)
    {
        LastCheckInAt = now;
        Describe(description);
    }

    private void Describe(HostDescription description)
    {
        Hostname = description.Hostname;
        Os = description.Os;
        Arch = description.Arch;
        RestOMaticVersion = description.RestOMaticVersion;
        ResticVersion = description.ResticVersion;
    }
}

/// <summary>What a host says about itself in every message.</summary>
public sealed record HostDescription(string Hostname, string Os, string Arch, string RestOMaticVersion, string? ResticVersion);

/// <summary>The three parts of a check-in, by the names the contract uses.</summary>
public static class PartKinds
{
    public const string Status = "status";
    public const string Snapshots = "snapshots";
    public const string Config = "config";

    public static readonly string[] All = [Status, Snapshots, Config];
}

/// <summary>
/// The latest copy of one part of a host's check-in, as the host sent it:
/// raw JSON for <c>status</c> and <c>snapshots</c>, the file's text for
/// <c>config</c>. History is not kept.
/// </summary>
public sealed class HostPart
{
    public Guid HostId { get; init; }

    public required string Kind { get; init; }

    /// <summary>The host's fingerprint of <see cref="Content"/>; opaque here.</summary>
    public string Fingerprint { get; set; } = "";

    public string? Content { get; set; }

    /// <summary>When the check-in this copy came from was sent.</summary>
    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Config only: false while the host withholds its current config.</summary>
    public bool IsCurrent { get; set; } = true;

    public string? WithheldReason { get; set; }

    /// <summary>Config only: the fields that keep it withheld, as a JSON array.</summary>
    public string? WithheldFields { get; set; }
}
