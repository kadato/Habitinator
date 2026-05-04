using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface IUserDateFormatService
{
    string DateFormat { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    string Format(DateOnly date);
}
