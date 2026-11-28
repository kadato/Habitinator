namespace App.Web.E2E;

/// <summary>Shared Playwright setup. Set <c>E2E_BASE_URL</c> to the running web app.</summary>
internal static class WebE2E
{
    private static bool? _isBaseUrlReachable;

    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/')
        ?? "http://127.0.0.1:5050";

    public static async Task EnsureBaseUrlReachableAsync()
    {
        if (_isBaseUrlReachable.HasValue)
        {
            Skip.If(!_isBaseUrlReachable.Value, $"E2E_BASE_URL '{BaseUrl}' is not reachable (cached check).");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var res = await client.GetAsync($"{BaseUrl}/health", cts.Token);
            if (res.IsSuccessStatusCode)
            {
                _isBaseUrlReachable = true;
                return;
            }
        }
        catch
        {
            // Fall through to the skip below.
        }

        _isBaseUrlReachable = false;
        Skip.If(true, $"E2E_BASE_URL '{BaseUrl}' is not reachable. Start App.Web before running Playwright tests.");
    }
}
