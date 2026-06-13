namespace App.Shared.RCL.Services;

public interface IClientSessionProvider
{
    bool IsLoggedIn { get; }
    event Action? Changed;
}
