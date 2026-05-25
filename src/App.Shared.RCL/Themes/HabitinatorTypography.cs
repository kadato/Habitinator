using MudBlazor;

namespace App.Shared.RCL.Themes;

internal static class HabitinatorTypography
{
    private static readonly string[] FontFamily =
    [
        "Plus Jakarta Sans",
        "ui-sans-serif",
        "system-ui",
        "-apple-system",
        "Segoe UI",
        "Roboto",
        "sans-serif"
    ];

    public static Typography Create() => new()
    {
        Default = WithFont<DefaultTypography>(),
        H1 = WithFont<H1Typography>(),
        H2 = WithFont<H2Typography>(),
        H3 = WithFont<H3Typography>(),
        H4 = WithFont<H4Typography>(),
        H5 = WithFont<H5Typography>(),
        H6 = WithFont<H6Typography>(),
        Subtitle1 = WithFont<Subtitle1Typography>(),
        Subtitle2 = WithFont<Subtitle2Typography>(),
        Body1 = WithFont<Body1Typography>(),
        Body2 = WithFont<Body2Typography>(),
        Button = WithFont<ButtonTypography>(),
        Caption = WithFont<CaptionTypography>(),
        Overline = WithFont<OverlineTypography>()
    };

    private static T WithFont<T>() where T : BaseTypography, new() => new() { FontFamily = FontFamily };
}
