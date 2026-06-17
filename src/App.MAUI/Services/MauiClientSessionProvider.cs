using System;
using App.Shared.RCL.Services;

namespace App.MAUI.Services;

public sealed class MauiClientSessionProvider : IClientSessionProvider
{
    private readonly IApiSession _session;

    public MauiClientSessionProvider(IApiSession session)
    {
        _session = session;
        _session.Changed += OnSessionChanged;
    }

    public bool IsLoggedIn => _session.IsLoggedIn;

    public string? Email => _session.Email;

    public event Action? Changed;

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        Changed?.Invoke();
    }
}
