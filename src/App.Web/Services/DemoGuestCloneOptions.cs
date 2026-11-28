using System.ComponentModel.DataAnnotations;

namespace App.Web.Services;

/// <summary>Controls temporary per-visitor copies of the demo board.</summary>
public sealed class DemoGuestCloneOptions
{
    public const string SectionName = "DemoGuestClone";

    /// <summary>How long a guest copy survives after guest login. Default is 24 hours.</summary>
    [Range(1, 43_200)]
    public int LifetimeMinutes { get; set; } = 24 * 60;

    /// <summary>How often expired copies are removed. Default is 15 minutes.</summary>
    [Range(1, 1_440)]
    public int CleanupIntervalMinutes { get; set; } = 15;

    /// <summary>Soft cap on active guest copies. The oldest copy is removed when the cap is reached.</summary>
    [Range(1, 5_000)]
    public int MaxActiveClones { get; set; } = 500;
}
