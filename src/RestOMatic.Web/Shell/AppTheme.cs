using MudBlazor;

namespace RestOMatic.Web.Shell;

public static class AppTheme
{
    // MudBlazor's default font is Roboto, which its documented setup loads
    // from Google Fonts. This app must not make a browser call another
    // origin, so every text style uses fonts the system already has.
    private static readonly string[] SystemFonts =
    [
        "system-ui", "-apple-system", "Segoe UI", "Helvetica", "Arial", "sans-serif",
    ];

    public static MudTheme Default { get; } = Create();

    private static MudTheme Create()
    {
        var typography = new Typography();
        BaseTypography[] styles =
        [
            typography.Default,
            typography.H1, typography.H2, typography.H3, typography.H4, typography.H5, typography.H6,
            typography.Subtitle1, typography.Subtitle2,
            typography.Body1, typography.Body2,
            typography.Button, typography.Caption, typography.Overline,
        ];
        foreach (var style in styles)
        {
            style.FontFamily = SystemFonts;
        }

        return new MudTheme { Typography = typography };
    }
}
