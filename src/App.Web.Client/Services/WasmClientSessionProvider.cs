using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using App.Shared.RCL.Services;

namespace App.Web.Client.Services;

public sealed class WasmClientSessionProvider : IClientSessionProvider, IDisposable
{
    private readonly AuthenticationStateProvider _auth;

    public WasmClientSessionProvider(AuthenticationStateProvider auth)
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
