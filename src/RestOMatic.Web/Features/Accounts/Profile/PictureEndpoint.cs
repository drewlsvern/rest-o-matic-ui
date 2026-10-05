namespace RestOMatic.Web.Features.Accounts.Profile;

/// <summary>
/// Serves profile pictures to signed-in users. The version in the URL
/// changes with every upload, so a picture can be cached for good.
/// </summary>
public static class PictureEndpoint
{
    public const string Pattern = "/account/users/{id:guid}/picture";

    public static string UrlFor(UserSummary user) => $"/account/users/{user.Id}/picture?v={user.PictureVersion}";

    public static void MapPictures(this IEndpointRouteBuilder endpoints)
    {
        // No AllowAnonymous: the fallback policy requires a signed-in user.
        endpoints.MapGet(Pattern, async (Guid id, HttpContext context, UserAccounts accounts) =>
        {
            var picture = await accounts.FindPictureAsync(id);
            if (picture is null)
            {
                return Results.NotFound();
            }

            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.ContentDisposition = "inline";
            headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.File(picture.Data, picture.ContentType);
        });
    }
}
