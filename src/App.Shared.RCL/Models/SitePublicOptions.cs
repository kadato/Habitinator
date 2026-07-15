namespace App.Shared.RCL.Models;

public sealed class SitePublicOptions
{
    public const string SectionName = "Site";

    public string PublicBaseUrl { get; set; } = "https://habitinator.app";

    public string SecurityContact { get; set; } = "mailto:security@habitinator.app";

    public string SecurityPolicyUrl { get; set; } =
        "https://github.com/tothKarolyDavid/Habitinator/security/policy";

    public string RepositoryUrl { get; set; } = "https://github.com/tothKarolyDavid/Habitinator";

    public Uri PublicBaseUri => new(PublicBaseUrl.TrimEnd('/') + "/");
}
