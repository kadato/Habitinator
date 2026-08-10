using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>Complete snapshot of the user's data for personal export.</summary>
public sealed record UserDataExportDto(
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<BoardItem> Items,
    IReadOnlyList<UserActivityEventRecord> Events);

public interface IUserDataExportService
{
    Task<UserDataExportDto> ExportAsync(CancellationToken cancellationToken = default);
}
