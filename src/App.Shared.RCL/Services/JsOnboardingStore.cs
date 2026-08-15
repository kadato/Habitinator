namespace App.Shared.RCL.Services;

/// <summary>Remembers whether the first-run onboarding dialog was shown for the signed-in account.</summary>
public sealed class JsOnboardingStore : IOnboardingStore
{
    private const string BaseKey = "habitinator.onboarding.v1";
    private readonly ILocalSettingsStore _localStore;
    private readonly IClientSessionProvider _sessionProvider;

    public JsOnboardingStore(ILocalSettingsStore localStore, IClientSessionProvider sessionProvider)
    {
        _localStore = localStore;
        _sessionProvider = sessionProvider;
    }

    private string GetKey() => LocalFirstRemoteStore.KeyFor(_sessionProvider.Email, BaseKey);

    public Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default)
    {
        var val = _localStore.Read(GetKey());
        return Task.FromResult(val is "1" or "true");
    }

    public Task MarkCompletedAsync(CancellationToken cancellationToken = default)
    {
        _localStore.Write(GetKey(), "1");
        return Task.CompletedTask;
    }
}
