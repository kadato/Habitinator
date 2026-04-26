using Microsoft.Extensions.Configuration;

namespace App.MAUI;

public static class MauiAppSettings
{
    /// <summary>Set by AppHost on the MAUI process to the <c>app-web</c> HTTP endpoint (see AppHost <c>Program.cs</c>).</summary>
    internal const string EnvApiBaseUrl = "HABITINATOR_API_BASE_URL";

    /// <summary>
    ///     Resolves the App.Web API origin (no trailing slash). The mobile app does not host the API:
    ///     run <c>App.Web</c> (or Aspire AppHost, which sets <see cref="EnvApiBaseUrl" />), or override via env/appsettings.
    /// </summary>
    public static string ResolveApiBaseUrl(IConfiguration configuration)
    {
        var env = Environment.GetEnvironmentVariable(EnvApiBaseUrl);
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim().TrimEnd('/');

        // Optional override; omit in appsettings so Android uses 10.0.2.2 and Windows uses 127.0.0.1.
        var fromConfig = configuration["Api:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig.Trim().TrimEnd('/');

        return DefaultApiBaseUrlNoSlash();
    }

    /// <summary>Fallback when env and appsettings do not set <c>Api:BaseUrl</c>.</summary>
    public static string DefaultApiBaseUrlNoSlash() =>
#if ANDROID
        "http://10.0.2.2:5031";
#else
        "http://127.0.0.1:5031";
#endif
}
