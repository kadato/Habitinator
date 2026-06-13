namespace App.Shared.RCL.Services;

public interface ILocalSettingsStore
{
    string? Get(string key, string? defaultValue = null);
    void Set(string key, string value);
}
