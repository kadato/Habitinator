namespace App.Shared.RCL.Services;

public interface ILocalSettingsStore
{
    string? Read(string key, string? defaultValue = null);
    void Write(string key, string value);
}
