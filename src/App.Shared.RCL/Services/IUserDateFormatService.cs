using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface IUserDateFormatService
{
    string DateFormat { get; }

    /// <summary>Applies date format from already-loaded preferences without another store round-trip.</summary>
    void ApplyFromPreferences(UserPreferences preferences);

    Task InitializeAsync(CancellationToken cancellationToken = default);

    string Format(DateOnly dateValue);

    string Format(DateTime dateTime);

    string Format(DateTimeOffset dateTimeOffset);
}
