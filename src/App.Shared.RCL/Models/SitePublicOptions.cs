using System.ComponentModel.DataAnnotations;

namespace App.Shared.RCL.Models;

public sealed class SitePublicOptions
{
    public const string SectionName = "Site";

    [Required]
    public Uri PublicBaseUrl { get; set; } = new("https://habitinator.app");

    [Required]
    public string SecurityContact { get; set; } = "mailto:security@habitinator.app";

    [Required]
    public Uri SecurityPolicyUrl { get; set; } =
        new("https://github.com/kadato/Habitinator/security/policy");

    [Required]
    public Uri RepositoryUrl { get; set; } = new("https://github.com/kadato/Habitinator");

    public Uri PublicBaseUri => PublicBaseUrl;
}
