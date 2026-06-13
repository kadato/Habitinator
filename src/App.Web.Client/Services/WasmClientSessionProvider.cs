using System;
using Microsoft.AspNetCore.Components.Authorization;
using App.Shared.RCL.Services;

namespace App.Web.Client.Services;

public sealed class WasmClientSessionProvider : IClientSessionProvider
{
    private readonly AuthenticationStateProvider _auth;

    public WasmClientSessionProvider(AuthenticationStateProvider auth)
    {
        _auth = auth;
    }

    public bool IsLoggedIn
    {
        get
        {
            if (_auth is WasmAuthenticationStateProvider wasmAuth)
            {
                var stateTask = wasmAuth.GetAuthenticationStateAsync();
                if (stateTask.IsCompletedSuccessfully)
                {
                    return stateTask.Result.User.Identity?.IsAuthenticated == true;
                }
            }
            return false;
        }
    }

    public event Action? Changed;
}
