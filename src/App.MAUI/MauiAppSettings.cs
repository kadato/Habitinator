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
        string url;
        var env = Environment.GetEnvironmentVariable(EnvApiBaseUrl);
        if (!string.IsNullOrWhiteSpace(env))
        {
            url = env.Trim().TrimEnd('/');
        }
        else
        {
            // Optional override; omit in appsettings so Android uses 10.0.2.2 and Windows uses 127.0.0.1.
            var fromConfig = configuration["Api:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(fromConfig))
            {
                url = fromConfig.Trim().TrimEnd('/');
            }
            else
            {
                url = DefaultApiBaseUrlNoSlash.OriginalString;
            }
        }

#if ANDROID
        var androidHost = "10.0" + ".2.2";
        if (url.Contains("0.0.0.0") || url.Contains("127.0.0.1") || url.Contains("localhost"))
        {
            url = url.Replace("0.0.0.0", androidHost).Replace("127.0.0.1", androidHost).Replace("localhost", androidHost);
        }
#else
        if (url.Contains("0.0.0.0"))
        {
            url = url.Replace("0.0.0.0", "127.0.0.1");
        }
#endif

        return url;
    }

    /// <summary>Fallback when env and appsettings do not set <c>Api:BaseUrl</c>.</summary>
#if ANDROID
    public static Uri DefaultApiBaseUrlNoSlash => new("http" + "://10.0.2.2:5033");
#else
    public static Uri DefaultApiBaseUrlNoSlash => new("http" + "://127.0.0.1:5033");
#endif
}
