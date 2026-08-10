using MudBlazor;

namespace App.Shared.RCL.Models;

public static class BoardSectionVisuals
{
    /// <summary>Matches target type string representation.</summary>
    public static string GetMudIconForTargetType(string? targetType)
    {
        return targetType switch
        {
            "Habit" => Icons.Material.Filled.FitnessCenter,
            "Daily" => Icons.Material.Filled.Today,
            "Todo" => Icons.Material.Filled.Checklist,
            "Session" => Icons.Material.Filled.Flag,
            _ => Icons.Material.Outlined.Label
        };
    }
}
