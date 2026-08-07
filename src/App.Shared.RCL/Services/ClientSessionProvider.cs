using System.Security.Claims;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Shared.RCL.Services;

public sealed class ClientSessionProvider : IClientSessionProvider, IDisposable
{
    private readonly AuthenticationStateProvider _auth;
    private AuthenticationState? _lastState;

    public ClientSessionProvider(AuthenticationStateProvider auth)
    {
        _auth = auth;
        _auth.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _ = RefreshAsync();
    }

    public bool IsLoggedIn => _lastState?.User.Identity?.IsAuthenticated == true;

    public string? Email
    {
        get
        {
            if (_lastState is not { } state)
            {
                return null;
            }

            var user = state.User;
            return user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity?.Name;
        }
    }

    public event EventHandler? Changed;

    private async Task RefreshAsync()
    {
        try
        {
            _lastState = await _auth.GetAuthenticationStateAsync().ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Keep the previous state; the next change notification retries.
        }
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        _ = ObserveStateAsync(task);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ObserveStateAsync(Task<AuthenticationState> task)
    {
        try
        {
            _lastState = await task.ConfigureAwait(false);
        }
        catch
        {
            // Keep the previous state; the next change notification retries.
        }
    }

    public void Dispose()
    {
        _auth.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }
}
