namespace App.Shared.RCL.Services;

public interface ITimerSessionLogService
{
    /// <summary>
    ///     Logs a stopped timer session to activity statistics and, when the target is a board row,
    ///     applies the same progress action as Stop and log on the board.
    /// </summary>
    Task<TimerSessionLogResult> LogStoppedSessionAsync(TimeSpan duration, CancellationToken cancellationToken = default);
}

public sealed record TimerSessionLogResult(bool BoardUpdateFailed, bool BoardProgressed, string UserMessage);
