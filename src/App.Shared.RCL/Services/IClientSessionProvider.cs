namespace App.Shared.RCL.Services;

public interface IClientSessionProvider
{
    bool IsLoggedIn { get; }
    string? Email { get; }
    event EventHandler? Changed;
}
