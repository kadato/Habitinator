using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

/// <summary>Saves and restores the timer session in the local settings store.</summary>
public sealed class TimerSessionStore(
    ILocalSettingsStore settings,
    ILogger<TimerSessionStore>? logger = null)
{
    private const string Key = "habitinator.timer.session";

    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = SharedJsonSerializerContext.Default
    };

    /// <summary>Writes the live session. Writes blank when the timer is idle, clearing the last session.</summary>
    public void Save(GlobalTimerService timer)
    {
        try
        {
            var snapshot = timer.CaptureState();
            if (IsIdle(snapshot))
            {
                settings.Write(Key, string.Empty);
                return;
            }

            settings.Write(Key, JsonSerializer.Serialize(snapshot, Serializer));
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Timer session save failed. The session restarts empty.");
        }
    }

    private static bool IsIdle(TimerSessionSnapshot snapshot) =>
        snapshot.ElapsedTicks <= 0
        && snapshot.TargetId is null
        && snapshot.FocusAlertAfterTicks is null
        && snapshot.CompletedIntervals <= 0
        && (snapshot.PomodoroStateName is null || snapshot.PomodoroStateName == nameof(PomodoroState.Idle));

    /// <summary>Restores the saved session paused. Returns false when nothing was saved.</summary>
    public bool TryRestore(GlobalTimerService timer)
    {
        try
        {
            var raw = settings.Read(Key);
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            var snapshot = JsonSerializer.Deserialize<TimerSessionSnapshot>(raw, Serializer);
            return timer.RestoreState(snapshot);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Timer session restore failed. The session starts empty.");
            return false;
        }
    }
}
