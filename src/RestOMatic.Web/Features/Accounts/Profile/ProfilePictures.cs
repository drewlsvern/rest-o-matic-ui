namespace RestOMatic.Web.Features.Accounts.Profile;

/// <summary>
/// Which uploads are accepted as a profile picture. The type is judged from
/// the file's first bytes, never from its name or the type the browser
/// claims. SVG is never accepted, because it can carry script.
/// </summary>
public static class ProfilePictures
{
    public const long MaxBytes = 1024 * 1024;

    public const string AllowedFormats = "PNG, JPEG or WebP";

    /// <summary>For the file picker only; the content is checked regardless.</summary>
    public const string Accept = ".png,.jpg,.jpeg,.webp,image/png,image/jpeg,image/webp";

    /// <summary>
    /// Reads at most <see cref="MaxBytes"/> plus one byte, so a larger
    /// upload is refused without being read whole.
    /// </summary>
    public static async Task<(string? Problem, string? ContentType, byte[]? Data)> ReadAsync(Stream upload)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await upload.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes)
            {
                return (TooLarge, null, null);
            }
        }

        var data = buffer.ToArray();
        var contentType = Detect(data);
        return contentType is null ? (WrongFormat, null, null) : (null, contentType, data);
    }

    public static string TooLarge => "A picture can be at most 1 MB.";

    public static string WrongFormat => $"A picture must be a {AllowedFormats} image.";

    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }
        if (data.Length >= 12
            && data[..4].SequenceEqual("RIFF"u8)
            && data[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }
        return null;
    }
}
