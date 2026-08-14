using Microsoft.Extensions.Configuration;

namespace App.MAUI;

public static class MauiAppSettings
{
    /// <summary>Set by AppHost on the MAUI process to the <c>app-web</c> HTTP endpoint. See AppHost <c>Program.cs</c>.</summary>
    internal const string EnvApiBaseUrl = "HABITINATOR_API_BASE_URL";

    /// <summary>
    ///     Resolves the App.Web API origin with no trailing slash. The mobile app does not host the API.
    ///     An optional override comes from the <see cref="EnvApiBaseUrl" /> environment variable.
    ///     Otherwise the value comes from <c>Api:BaseUrl</c> in appsettings, with platform defaults in
    ///     <c>appsettings.json</c> and <c>appsettings.Android.json</c>.
    /// </summary>
    public static string ResolveApiBaseUrl(IConfiguration configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvApiBaseUrl);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim().TrimEnd('/');
        }

        return configuration["Api:BaseUrl"]?.Trim().TrimEnd('/')
            ?? throw new InvalidOperationException("Api:BaseUrl is not configured in appsettings.");
    }
}
