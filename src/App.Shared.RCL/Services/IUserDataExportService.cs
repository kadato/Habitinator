using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
/// Complete snapshot of the user's data for personal export and restore.
/// <see cref="Items" /> carries each item with its section since dates alone cannot
/// tell a daily from a to-do. <see cref="ExportedForLocalDay" /> anchors the local
/// day the snapshot was computed for so readers interpret the dates correctly.
/// </summary>
public sealed record UserDataExportDto(
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<BoardSyncItem> Items,
    IReadOnlyList<UserActivityEventRecord> Events,
    UserPreferences? Preferences = null,
    NotificationSettings? NotificationSettings = null,
    DateOnly ExportedForLocalDay = default);

public interface IUserDataExportService
{
    Task<UserDataExportDto> ExportAsync(CancellationToken cancellationToken = default);
}

/// <summary>Counts from a restore. Re-importing the same file yields the same board.</summary>
public sealed record UserDataImportResult(
    int Items,
    int Events,
    bool PreferencesRestored,
    bool NotificationsRestored);

public interface IUserDataImportService
{
    /// <param name="json">Export file contents, current or legacy shape.</param>
    Task<UserDataImportResult> ImportAsync(string json, CancellationToken cancellationToken = default);
}
