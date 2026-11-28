namespace App.Shared.RCL.Models;

/// <summary>Asks the server which models a provider currently lists.</summary>
public sealed class AiModelsRequest
{
    public string Provider { get; set; } = AiPreferences.DefaultProvider;

    public string Endpoint { get; set; } = AiPreferences.DefaultOpenCodeGoEndpoint;

    /// <summary>Optional key typed in Settings but not saved yet.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
///     The provider's model list. <see cref="Source" /> is <c>provider</c> when the
///     provider answered and <c>catalog</c> when the request fell back to the
///     built-in list.
/// </summary>
public sealed class AiModelsResponse
{
    public bool Success { get; set; }

    public string Source { get; set; } = "catalog";

    public List<AiModelOption> Models { get; set; } = [];

    public string? Error { get; set; }
}
