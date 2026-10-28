namespace App.Shared.RCL.Services;

/// <summary>Pending local settings writes the server has not acknowledged yet.</summary>
public interface ISettingsSyncFlush
{
    Task FlushPendingAsync(CancellationToken cancellationToken = default);

    bool HasPending();
}
