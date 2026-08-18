namespace App.Shared.RCL.Models;

public sealed class SitePublicOptions
{
    public const string SectionName = "Site";

    public Uri PublicBaseUrl { get; set; } = new("https://habitinator.app");

    public string SecurityContact { get; set; } = "mailto:security@habitinator.app";

    public Uri SecurityPolicyUrl { get; set; } =
        new("https://github.com/kadato/Habitinator/security/policy");

    public Uri RepositoryUrl { get; set; } = new("https://github.com/kadato/Habitinator");

    public Uri PublicBaseUri => PublicBaseUrl;
}
