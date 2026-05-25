using MudBlazor;

namespace App.Shared.RCL.Themes;

/// <summary>
///     Dark and light palettes aligned with premium styling requirements.
/// </summary>
public static class HabitinatorMudTheme
{
    public static readonly MudTheme Default = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#3b82f6",
            PrimaryContrastText = "#ffffff",
            Secondary = "#64748b",
            SecondaryContrastText = "#ffffff",
            Tertiary = "#a78bfa",
            Black = "#0f172a",
            Background = "#f8fafc",
            BackgroundGray = "#f1f5f9",
            Surface = "#ffffff",
            DrawerBackground = "#ffffff",
            DrawerText = "#1e293b",
            AppbarBackground = "#ffffff",
            AppbarText = "#1e293b",
            TextPrimary = "#0f172a",
            TextSecondary = "#475569",
            TextDisabled = "#94a3b8",
            ActionDefault = "#475569",
            Divider = "#e2e8f0",
            DividerLight = "#f1f5f9",
            TableLines = "#e2e8f0",
            LinesDefault = "#e2e8f0",
            Dark = "#0f172a",
            DarkContrastText = "#ffffff"
        },
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
            DarkContrastText = "#e5e7eb"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "0.375rem"
        },
        Typography = HabitinatorTypography.Create()
    };
}
