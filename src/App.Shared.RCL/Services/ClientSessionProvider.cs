using System.Security.Claims;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Shared.RCL.Services;

public sealed class ClientSessionProvider : IClientSessionProvider, IDisposable
{
    private readonly AuthenticationStateProvider _auth;

    public ClientSessionProvider(AuthenticationStateProvider auth)
    {
        _auth = auth;
        _auth.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    public bool IsLoggedIn
    {
        get
        {
            var stateTask = _auth.GetAuthenticationStateAsync();
            if (stateTask.IsCompletedSuccessfully)
            {
                return stateTask.Result.User.Identity?.IsAuthenticated == true;
            }
            return false;
        }
    }

    public string? Email
    {
        get
        {
            var stateTask = _auth.GetAuthenticationStateAsync();
            if (stateTask.IsCompletedSuccessfully)
            {
                var user = stateTask.Result.User;
                return user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity?.Name;
            }
            return null;
        }
    }

    public event EventHandler? Changed;

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _auth.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }
}
