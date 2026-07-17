using System.Security.Claims;

using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebClientSessionProvider : IClientSessionProvider, IDisposable
{
    private readonly AuthenticationStateProvider _auth;

    public WebClientSessionProvider(AuthenticationStateProvider auth)
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

    public event Action? Changed;

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _auth.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }
}
