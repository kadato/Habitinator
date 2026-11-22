using App.Shared.RCL.Services;

namespace App.Shared.RCL.Tests;

/// <summary>Deterministic day service for bUnit component tests.</summary>
internal sealed class TestUserDayService : IUserDayService
{
#pragma warning disable CS0067 // Raised explicitly by tests that exercise rollover.
    public event EventHandler? DayChanged;
#pragma warning restore CS0067

    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public TimeSpan DayStartLocalTime { get; set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void CheckForDayChange()
    {
    }

    public void RaiseDayChanged() => DayChanged?.Invoke(this, EventArgs.Empty);
}
