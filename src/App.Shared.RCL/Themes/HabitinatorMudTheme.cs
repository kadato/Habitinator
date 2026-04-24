using MudBlazor;

namespace App.Shared.RCL.Themes;

/// <summary>
/// Dark palette aligned with app shell CSS (<c>#030712</c> background, <c>#111827</c> surfaces).
/// </summary>
public static class HabitinatorMudTheme
{
    public static readonly MudTheme Default = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#3b82f6",
            PrimaryContrastText = "#ffffff",
            Secondary = "#64748b",
            SecondaryContrastText = "#f8fafc",
            Tertiary = "#a78bfa",
            Black = "#030712",
            Background = "#030712",
            BackgroundGray = "#1f2937",
            Surface = "#111827",
            DrawerBackground = "#111827",
            DrawerText = "#e5e7eb",
            AppbarBackground = "#111827",
            AppbarText = "#e5e7eb",
            TextPrimary = "#e5e7eb",
            TextSecondary = "#9ca3af",
            TextDisabled = "#6b7280",
            ActionDefault = "#9ca3af",
            Divider = "#374151",
            DividerLight = "#1f2937",
            TableLines = "#374151",
            LinesDefault = "#374151",
            Dark = "#030712",
            DarkContrastText = "#e5e7eb",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "0.375rem",
        },
    };
}
