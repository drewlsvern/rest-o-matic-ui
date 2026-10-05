using System.Net;
using System.Text;
using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Accounts.Profile;

namespace RestOMatic.Web.Tests.Features.Accounts;

public class ProfilePictureTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
    private static readonly byte[] WebP = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 1, 2, 3, 4];

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public void Allowed_formats_are_recognised_by_their_content(string format, string contentType)
    {
        var data = format switch { "png" => Png, "jpeg" => Jpeg, _ => WebP };

        Assert.Equal(contentType, ProfilePictures.Detect(data));
    }

    [Fact]
    public async Task Upload_replace_and_remove()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");

        Assert.True((await UploadAsync(factory, alice, Png)).Succeeded);
        var first = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        Assert.True(first!.HasPicture);

        Assert.True((await UploadAsync(factory, alice, Jpeg)).Succeeded);
        var second = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        Assert.NotEqual(PictureEndpoint.UrlFor(first), PictureEndpoint.UrlFor(second!));
        Assert.Equal("image/jpeg", (await factory.WithAccountsAsync(accounts => accounts.FindPictureAsync(alice.Id)))!.ContentType);

        Assert.True((await factory.WithAccountsAsync(accounts => accounts.RemovePictureAsync(alice.Id))).Succeeded);
        var removed = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        Assert.False(removed!.HasPicture);
        Assert.Null(await factory.WithAccountsAsync(accounts => accounts.FindPictureAsync(alice.Id)));

        // A picture uploaded after removal never reuses an earlier address.
        await UploadAsync(factory, alice, Png);
        var again = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        Assert.NotEqual(PictureEndpoint.UrlFor(first), PictureEndpoint.UrlFor(again!));
    }

    [Fact]
    public async Task A_picture_over_1_MB_is_refused_and_the_old_one_kept()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        await UploadAsync(factory, alice, Png);

        var tooLarge = new byte[2 * 1024 * 1024];
        Png.CopyTo(tooLarge, 0);
        var result = await UploadAsync(factory, alice, tooLarge);

        Assert.Equal(["A picture can be at most 1 MB."], result.Problems);
        Assert.Equal("image/png", (await factory.WithAccountsAsync(accounts => accounts.FindPictureAsync(alice.Id)))!.ContentType);
    }

    [Fact]
    public async Task A_picture_of_exactly_1_MB_is_accepted()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        var data = new byte[ProfilePictures.MaxBytes];
        Png.CopyTo(data, 0);

        Assert.True((await UploadAsync(factory, alice, data)).Succeeded);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("hello, I am a text file called me.png")]
    [InlineData("GIF89a......")]
    public async Task Anything_else_is_refused(string content)
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");

        var result = await UploadAsync(factory, alice, Encoding.UTF8.GetBytes(content));

        Assert.Equal(["A picture must be a PNG, JPEG or WebP image."], result.Problems);
        Assert.False((await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id)))!.HasPicture);
    }

    [Fact]
    public async Task The_picture_is_served_to_signed_in_users_with_safe_headers()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        await UploadAsync(factory, alice, Png);
        var summary = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        using var client = await factory.CreateSignedInClientAsync(alice);

        using var response = await client.GetAsync(PictureEndpoint.UrlFor(summary!));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_picture_is_not_served_to_anyone_signed_out()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        await UploadAsync(factory, alice, Png);
        var summary = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        using var client = factory.CreateDirectClient();

        using var response = await client.GetAsync(PictureEndpoint.UrlFor(summary!));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task No_picture_is_404()
    {
        using var factory = new AppFactory();
        var alice = await factory.CreateUserAsync("alice");
        var summary = await factory.WithAccountsAsync(accounts => accounts.FindAsync(alice.Id));
        using var client = await factory.CreateSignedInClientAsync(alice);

        using var response = await client.GetAsync(PictureEndpoint.UrlFor(summary!));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Alice Smith", "AS")]
    [InlineData("bob", "B")]
    [InlineData("jane.doe", "JD")]
    [InlineData("Ana María López", "AM")]
    public void Initials(string name, string expected)
    {
        Assert.Equal(expected, UserAvatar.Initials(name));
    }

    private static Task<AccountResult> UploadAsync(AppFactory factory, User user, byte[] data) =>
        factory.WithAccountsAsync(accounts => accounts.SetPictureAsync(user.Id, new MemoryStream(data)));
}
