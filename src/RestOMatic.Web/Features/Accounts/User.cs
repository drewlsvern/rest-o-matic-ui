namespace RestOMatic.Web.Features.Accounts;

/// <summary>
/// Someone who can sign in. How they sign in is held apart from the user
/// (<see cref="PasswordCredential"/> today, external logins later), so a user
/// can exist without a password.
/// </summary>
public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Fixed once the user exists.</summary>
    public required string UserName
    {
        get;
        init
        {
            field = value;
            NormalizedUserName = AccountRules.Normalize(value);
        }
    }

    public string NormalizedUserName { get; private set; } = "";

    /// <summary>Not verified: never used to find a user when signing in.</summary>
    public required string Email
    {
        get;
        set
        {
            field = value;
            NormalizedEmail = AccountRules.Normalize(value);
        }
    }

    public string NormalizedEmail { get; private set; } = "";

    public string? DisplayName { get; set; }

    /// <summary>
    /// Changes whenever the user's existing sessions must end: a password
    /// change or reset. Each session carries the stamp it started with.
    /// </summary>
    public Guid SecurityStamp { get; private set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Bumped on every picture upload or removal, never reset, so a picture's URL is never reused.</summary>
    public int PictureVersion { get; private set; }

    public bool HasPicture { get; private set; }

    public PasswordCredential? Password { get; set; }

    public ProfilePicture? Picture { get; private set; }

    /// <summary>The name shown wherever the app says who someone is.</summary>
    public string ShownName => string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName;

    public void ChangeSecurityStamp() => SecurityStamp = Guid.NewGuid();

    public void SetPicture(string contentType, byte[] data)
    {
        if (Picture is null)
        {
            Picture = new ProfilePicture { UserId = Id, ContentType = contentType, Data = data };
        }
        else
        {
            Picture.ContentType = contentType;
            Picture.Data = data;
        }
        HasPicture = true;
        PictureVersion++;
    }

    public void RemovePicture()
    {
        Picture = null;
        HasPicture = false;
        PictureVersion++;
    }
}

public sealed class PasswordCredential
{
    public Guid UserId { get; init; }

    public required string Hash { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

public sealed class ProfilePicture
{
    public Guid UserId { get; init; }

    public required string ContentType { get; set; }

    public required byte[] Data { get; set; }
}
