namespace App.MAUI.Services;

/// <summary>Resolved API origin (same value used for HttpClient and SignalR). Trailing slash.</summary>
public sealed class MauiApiEndpointOptions
{
    public MauiApiEndpointOptions(string baseUrlWithTrailingSlash) =>
        BaseUrlWithTrailingSlash = baseUrlWithTrailingSlash;

    public string BaseUrlWithTrailingSlash { get; }
}
