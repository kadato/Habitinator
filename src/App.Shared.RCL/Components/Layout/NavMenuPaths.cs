using Microsoft.AspNetCore.Components.Routing;

namespace App.Shared.RCL.Components.Layout;

public static class NavMenuPaths
{
    public static bool IsActivePath(string relativeUri, string path, NavLinkMatch match)
    {
        var rel = relativeUri.TrimStart('/').TrimEnd('/');
        var p = path.TrimStart('/').TrimEnd('/');

        if (match == NavLinkMatch.All)
        {
            if (string.IsNullOrEmpty(p))
            {
                return string.IsNullOrEmpty(rel);
            }

            return string.Equals(rel, p, StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrEmpty(p))
        {
            return true;
        }

        return rel.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}
