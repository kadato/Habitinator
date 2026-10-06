using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;

using MudBlazor;

namespace App.Shared.RCL.Services.CommandPalette;

/// <summary>Focus timer catalog, search shortcuts, sessions, and the start wizard.</summary>
#pragma warning disable S107 // Palette fragment needs timer, board, dialogs, notifier, log, and navigation callbacks together.
internal sealed class TimerPaletteCommands(
    GlobalTimerService timer,
    IBoardDataService boardData,
    IDialogService dialogs,
    IUserNotifier notifier,
    ITimerSessionLogService? timerSessionLog,
    Func<string, Task> navigate,
    Action close,
    Func<Task> notifyRefresh)
{
    private readonly GlobalTimerService _timer = timer;
    private readonly IBoardDataService _boardData = boardData;
    private readonly IDialogService _dialogs = dialogs;
    private readonly IUserNotifier _notifier = notifier;
    private readonly ITimerSessionLogService? _timerSessionLog = timerSessionLog;
    private readonly Func<string, Task> _navigate = navigate;
    private readonly Action _close = close;
    private readonly Func<Task> _notifyRefresh = notifyRefresh;

    private const string SuggestedCategory = "Suggested";
    private const string ActionsCategory = "Actions";
    private const string TimerCategory = "Timer";
    private const string DurationCategory = "Duration";
    private const string ModeCategory = "Mode";
    private const string GeneralSessionLabel = "General Session";
    private const string ResetFocusTimerTitle = "Reset focus timer";
    private const string ResetPomodoroIdleSubtitle = "Reset Pomodoro intervals and return to idle";
    private const string ResetSessionZeroSubtitle = "Reset session timer to zero";
    private const string PomodoroResetIdleMessage = "Pomodoro session reset to idle.";
    private const string StopwatchResetZeroMessage = "Stopwatch timer reset to zero.";
    private const string KwTimer = "timer";
    private const string KwSession = "session";
    private const string KwVirtualSession = "virtual session";
    private const string KwFocus = "focus";
    private const string KwBreak = "break";
    private const string KwReset = "reset";
    private const string KwClear = "clear";
    private const string KwStart = "start";
    private const string KwTarget = "target";
    private const string KwDuration = "duration";
    private const string KwEmpty = "empty";
    private const string KwModraw = "modraw";
    private const string KwComodoro = "comodoro";
    private const string KwPomodoro = "pomodoro";
    private const string KwStopwatch = "stopwatch";
    private const string KwCustom = "custom";
    private const string KwContinuous = "continuous";
    private const string KwOpen = "open";
    private const string KwHabit = "habit";
    private const string KwDaily = "daily";
    private const string KwMinutes = "minutes";
    private const string KwTodo = "todo";
    private const string ShortcutS = "S";
    private const string TimerResetId = "timer-reset";
    private const string TimerStopId = "timer-stop";
    private const string StopFocusSessionTitle = "Stop focus session";
    private const string KwStop = "stop";
    private const string KwLog = "log";
    private const string KwLock = "lock";
    private const string KwEnd = "end";
    private const string KwZero = "zero";
    private const string KwWork = "work";
    private const string KwLabel = "label";
    private const string KwInterval = "interval";
    private const string Kw15 = "15";
    private const string Kw15M = "15m";
    private const string Kw25 = "25";
    private const string Kw25M = "25m";
    private const string Kw30 = "30";
    private const string Kw30M = "30m";
    private const string Kw45 = "45";
    private const string Kw45M = "45m";

    public void AddSuggestedTimerCommands(List<CommandItem> list)
    {
        if (_timer.IsRunning)
        {
            AddRunningTimerCommands(list);
            return;
        }

        if (IsOnBreak())
        {
            AddPausedBreakCommands(list);
            return;
        }

        if (HasResumableSession())
        {
            AddPausedResumeCommands(list);
            return;
        }

        AddIdleTimerCommands(list);
    }

    private bool IsOnBreak() =>
        _timer.PomodoroModeEnabled &&
        (_timer.CurrentPomodoroState == PomodoroState.ShortBreak || _timer.CurrentPomodoroState == PomodoroState.LongBreak);

    private bool HasResumableSession() =>
        _timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle);

    private void AddRunningTimerCommands(List<CommandItem> list)
    {
        list.Add(new(
            Id: TimerStopId,
            Title: StopFocusSessionTitle,
            Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)}",
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.Stop,
            ShortcutBadge: ShortcutS,
            Action: StopTimerSessionAsync,
            Keywords: [KwTimer, KwStop, KwSession, KwVirtualSession, KwFocus, KwLog, KwLock, KwEnd]));

        list.Add(new(
            Id: "timer-pause",
            Title: "Pause focus timer",
            Subtitle: _timer.PomodoroModeEnabled ? $"Pause active {_timer.StatusLabel}" : "Temporarily pause the running session",
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.Pause,
            Action: () =>
            {
                _close();
                _timer.Pause();
                return Task.CompletedTask;
            },
            Keywords: [KwTimer, "pause", KwSession, KwVirtualSession, KwFocus, KwBreak]));

        AddResetSuggestedCommand(list);

        if (IsOnBreak())
        {
            AddSkipBreakCommand(list, "Skip break and resume work", "End break early and start next Pomodoro work interval");
        }
    }

    private void AddPausedBreakCommands(List<CommandItem> list)
    {
        list.Add(new(
            Id: "timer-start-break",
            Title: "Start break countdown",
            Subtitle: $"Begin {_timer.StatusLabel}, {GlobalTimerService.FormatTimeSpan(_timer.FocusAlertAfter ?? _timer.ShortBreakDuration)}",
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.Coffee,
            ShortcutBadge: ShortcutS,
            Action: () =>
            {
                _close();
                _timer.Start();
                return Task.CompletedTask;
            },
            Keywords: [KwBreak, KwStart, KwTimer, "coffee", "rest"]));

        AddSkipBreakCommand(list, "Skip break and start work", "Skip break and start next Pomodoro work interval immediately");

        list.Add(new(
            Id: TimerStopId,
            Title: StopFocusSessionTitle,
            Subtitle: "Reset and end the active Pomodoro cycle",
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.Stop,
            Action: StopTimerSessionAsync,
            Keywords: [KwTimer, KwStop, KwSession, KwVirtualSession, KwEnd, KwLog, KwLock]));

        list.Add(new(
            Id: TimerResetId,
            Title: ResetFocusTimerTitle,
            Subtitle: ResetPomodoroIdleSubtitle,
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.Refresh,
            Action: async () =>
            {
                _close();
                _timer.ResetSession();
                await _notifier.NotifyAsync(PomodoroResetIdleMessage, Severity.Info);
            },
            Keywords: [KwTimer, KwReset, KwClear, KwZero]));
    }

    private void AddPausedResumeCommands(List<CommandItem> list)
    {
        var resumeLabel = _timer.PomodoroModeEnabled
            ? $"Resume Pomodoro, {_timer.GetDisplayTime()} remaining"
            : $"Resume focus session, {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} elapsed";

        list.Add(new(
            Id: "timer-resume",
            Title: "Resume focus session",
            Subtitle: resumeLabel,
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.PlayArrow,
            ShortcutBadge: ShortcutS,
            Action: () =>
            {
                _close();
                _timer.Start();
                return Task.CompletedTask;
            },
            Keywords: [KwTimer, "resume", KwStart, KwFocus, KwSession, KwVirtualSession]));

        list.Add(new(
            Id: TimerStopId,
            Title: StopFocusSessionTitle,
            Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)}",
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.Stop,
            Action: StopTimerSessionAsync,
            Keywords: [KwTimer, KwStop, KwSession, KwVirtualSession, KwFocus, KwLog, KwLock, KwEnd]));

        AddResetSuggestedCommand(list);
    }

    private void AddIdleTimerCommands(List<CommandItem> list)
    {
        var targetDesc = string.IsNullOrWhiteSpace(_timer.TargetId)
            ? "empty session"
            : $"target: {_timer.TargetId}";

        list.Add(new(
            Id: "timer-setup-and-start",
            Title: "Start focus session...",
            Subtitle: "Choose Stopwatch or Pomodoro, then select target and duration",
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.PlayCircleOutline,
            ChildrenProvider: GetTimerModeSelectionCommandsAsync,
            Keywords: [KwTimer, KwSession, KwVirtualSession, KwStart, KwTarget, KwFocus, KwDuration, "explore", KwEmpty, KwModraw, KwPomodoro, KwComodoro, KwStopwatch]));

        list.Add(new(
            Id: "timer-start-pomodoro",
            Title: "Quick start Pomodoro",
            Subtitle: $"Start {_timer.WorkDuration.TotalMinutes:0}m Pomodoro work interval, {targetDesc}",
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.Timelapse,
            ShortcutBadge: "P",
            Action: StartPomodoroSessionAsync,
            Keywords: [KwPomodoro, KwComodoro, KwModraw, "quick start pomodoro", KwStart, KwFocus, KwVirtualSession]));

        list.Add(new(
            Id: "timer-start",
            Title: "Start Stopwatch session",
            Subtitle: $"Start open stopwatch focus session, {targetDesc}",
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.PlayArrow,
            ShortcutBadge: ShortcutS,
            Action: async () =>
            {
                _close();
                _timer.PomodoroModeEnabled = false;
                _timer.Start();
                var tName = string.IsNullOrWhiteSpace(_timer.TargetId) ? GeneralSessionLabel : _timer.TargetId;
                await _notifier.NotifyAsync($"Stopwatch session started for '{tName}'.", Severity.Info);
            },
            Keywords: [KwTimer, KwStart, KwStopwatch, KwOpen, KwFocus, KwSession]));
    }

    private void AddResetSuggestedCommand(List<CommandItem> list)
    {
        list.Add(new(
            Id: TimerResetId,
            Title: ResetFocusTimerTitle,
            Subtitle: _timer.PomodoroModeEnabled ? ResetPomodoroIdleSubtitle : ResetSessionZeroSubtitle,
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.Refresh,
            Action: async () =>
            {
                _close();
                _timer.ResetSession();
                await _notifier.NotifyAsync(_timer.PomodoroModeEnabled ? PomodoroResetIdleMessage : StopwatchResetZeroMessage, Severity.Info);
            },
            Keywords: [KwTimer, KwReset, KwClear, KwZero]));
    }

    private void AddSkipBreakCommand(List<CommandItem> list, string title, string subtitle)
    {
        list.Add(new(
            Id: "timer-skip-break",
            Title: title,
            Subtitle: subtitle,
            Category: SuggestedCategory,
            Icon: Icons.Material.Filled.SkipNext,
            Action: async () =>
            {
                _close();
                _timer.TransitionToWork();
                _timer.Start();
                var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? GeneralSessionLabel : _timer.TargetId;
                await _notifier.NotifyAsync($"Break skipped. Work started for '{targetName}'.", Severity.Info);
            },
            Keywords: ["skip", KwBreak, KwWork, KwPomodoro]));
    }

    public void AddTimerActionCommands(List<CommandItem> list)
    {
        if (_timer.IsRunning || _timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle))
        {
            list.Add(new(
                Id: "timer-setup-and-start",
                Title: "Start new focus session...",
                Subtitle: "Choose Stopwatch or Pomodoro mode, then select target and duration",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.PlayCircleOutline,
                ChildrenProvider: GetTimerModeSelectionCommandsAsync,
                Keywords: [KwTimer, KwSession, KwVirtualSession, KwStart, KwTarget, KwFocus, KwDuration, "explore", KwEmpty, KwModraw, KwPomodoro, KwComodoro, KwStopwatch, KwHabit, KwDaily, KwTodo]));
        }

        var targetDisplay = string.IsNullOrWhiteSpace(_timer.TargetId) ? "None" : _timer.TargetId;
        list.Add(new(
            Id: "timer-set-target",
            Title: "Set session target...",
            Subtitle: $"Target: {targetDisplay} - Assign habit, daily, to-do, or custom label",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.AdsClick,
            ChildrenProvider: GetTimerTargetConfigurationCommandsAsync,
            Keywords: [KwTimer, KwTarget, KwFocus, "item", KwHabit, KwDaily, KwTodo, "custom target", KwLabel, "set target"]));

        string durationSubtitle;
        if (_timer.PomodoroModeEnabled)
        {
            durationSubtitle = $"Pomodoro mode active with {GlobalTimerService.FormatTimeSpan(_timer.WorkDuration)} interval. Select to configure.";
        }
        else if (_timer.FocusAlertAfter.HasValue)
        {
            durationSubtitle = $"Current alert: {GlobalTimerService.FormatTimeSpan(_timer.FocusAlertAfter.Value)}";
        }
        else
        {
            durationSubtitle = "Continuous count-up without alert";
        }

        list.Add(new(
            Id: "timer-set-duration",
            Title: "Set focus duration...",
            Subtitle: durationSubtitle,
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.HourglassTop,
            ChildrenProvider: GetDurationConfigurationCommandsAsync,
            Keywords: [KwTimer, KwDuration, "alert", "time's up", KwFocus, KwMinutes, "length", "custom duration"]));

        list.Add(new(
            Id: TimerResetId,
            Title: ResetFocusTimerTitle,
            Subtitle: _timer.PomodoroModeEnabled ? ResetPomodoroIdleSubtitle : ResetSessionZeroSubtitle,
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Refresh,
            Action: async () =>
            {
                _close();
                _timer.ResetSession();
                await _notifier.NotifyAsync("Focus timer reset.", Severity.Info);
            },
            Keywords: [KwTimer, KwReset, KwClear]));

        if (_timer.TargetId is not null)
        {
            list.Add(new(
                Id: "timer-clear-target",
                Title: "Clear timer target",
                Subtitle: $"Active target: {_timer.TargetId}",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.TimerOff,
                Action: async () =>
                {
                    _close();
                    _timer.SetManualTarget(null);
                    await _notifier.NotifyAsync("Timer target cleared.", Severity.Info);
                },
                Keywords: [KwTimer, "clear target", KwTarget, KwStop]));
        }

        list.Add(new(
            Id: "timer-toggle-pomodoro",
            Title: _timer.PomodoroModeEnabled ? "Disable Pomodoro mode" : "Enable Pomodoro mode",
            Subtitle: _timer.PomodoroModeEnabled ? "Switch to open Stopwatch focus mode" : "Switch to interval focus sessions with scheduled breaks",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Timelapse,
            Action: TogglePomodoroModeAsync,
            Keywords: [KwPomodoro, KwModraw, KwTimer, KwInterval, KwBreak, KwVirtualSession, "mode"]));
    }

    public void AddTimerShortcutCommands(string q, List<CommandItem> results)
    {
        AddResumeShortcutCommand(q, results);
        AddPauseShortcutCommand(q, results);
        AddStopShortcutCommand(q, results);
        AddResetShortcutCommand(q, results);
        AddPomodoroShortcutCommand(q, results);
        AddDurationShortcutCommands(q, results);
    }

    private void AddResumeShortcutCommand(string q, List<CommandItem> results)
    {
        if (!q.Equals("resume", StringComparison.OrdinalIgnoreCase) || _timer.IsRunning || !HasResumableSession())
        {
            return;
        }

        var resumeLabel = _timer.PomodoroModeEnabled
            ? $"Resume Pomodoro, {_timer.GetDisplayTime()} remaining"
            : $"Resume focus session, {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} elapsed";
        results.Add(new CommandItem(
            Id: "timer-resume-search",
            Title: "Resume focus session",
            Subtitle: resumeLabel,
            Category: TimerCategory,
            Icon: Icons.Material.Filled.PlayArrow,
            Action: () =>
            {
                _close();
                _timer.Start();
                return Task.CompletedTask;
            }));
    }

    private void AddPauseShortcutCommand(string q, List<CommandItem> results)
    {
        if (!q.Equals("pause", StringComparison.OrdinalIgnoreCase) || !_timer.IsRunning)
        {
            return;
        }

        results.Add(new CommandItem(
            Id: "timer-pause-search",
            Title: "Pause focus timer",
            Subtitle: _timer.PomodoroModeEnabled ? $"Pause active {_timer.StatusLabel}" : "Temporarily pause the running session",
            Category: TimerCategory,
            Icon: Icons.Material.Filled.Pause,
            Action: () =>
            {
                _close();
                _timer.Pause();
                return Task.CompletedTask;
            }));
    }

    private void AddStopShortcutCommand(string q, List<CommandItem> results)
    {
        if (!IsStopQuery(q) || !HasActiveOrResumableSession())
        {
            return;
        }

        results.Add(new CommandItem(
            Id: "timer-stop-search",
            Title: "Stop and log focus session",
            Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} to target",
            Category: TimerCategory,
            Icon: Icons.Material.Filled.Stop,
            Action: StopTimerSessionAsync));
    }

    private void AddResetShortcutCommand(string q, List<CommandItem> results)
    {
        if (!q.Equals("reset", StringComparison.OrdinalIgnoreCase) || !HasActiveOrResumableSession())
        {
            return;
        }

        results.Add(new CommandItem(
            Id: "timer-reset-search",
            Title: ResetFocusTimerTitle,
            Subtitle: _timer.PomodoroModeEnabled ? ResetPomodoroIdleSubtitle : ResetSessionZeroSubtitle,
            Category: TimerCategory,
            Icon: Icons.Material.Filled.Refresh,
            Action: async () =>
            {
                _close();
                _timer.ResetSession();
                await _notifier.NotifyAsync(_timer.PomodoroModeEnabled ? PomodoroResetIdleMessage : StopwatchResetZeroMessage, Severity.Info, CancellationToken.None);
            }));
    }

    private void AddPomodoroShortcutCommand(string q, List<CommandItem> results)
    {
        if (!IsPomodoroQuery(q))
        {
            return;
        }

        var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? GeneralSessionLabel : _timer.TargetId;
        results.Add(new CommandItem(
            Id: "quick-start-pomodoro-search",
            Title: "Start Pomodoro session",
            Subtitle: $"Start {_timer.WorkDuration.TotalMinutes:0}m work interval for {targetName}",
            Category: TimerCategory,
            Icon: Icons.Material.Filled.Timelapse,
            Action: StartPomodoroSessionAsync));
    }

    private void AddDurationShortcutCommands(string q, List<CommandItem> results)
    {
        var durationMatch = System.Text.RegularExpressions.Regex.Match(
            q,
            @"^(?:timer|focus|start|session)\s+(\d{1,3})(?:m|min)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));
        if (!durationMatch.Success || !int.TryParse(durationMatch.Groups[1].Value, out var parsedMinutes) || parsedMinutes <= 0)
        {
            return;
        }

        var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? GeneralSessionLabel : _timer.TargetId;
        results.Add(new CommandItem(
            Id: $"quick-start-duration-{parsedMinutes}",
            Title: $"Start {parsedMinutes}-minute focus session",
            Subtitle: $"Start focus timer immediately with alert after {parsedMinutes} minutes for '{targetName}'",
            Category: TimerCategory,
            Icon: Icons.Material.Filled.PlayArrow,
            Action: async () =>
            {
                _close();
                _timer.PomodoroModeEnabled = false;
                _timer.FocusAlertAfter = TimeSpan.FromMinutes(parsedMinutes);
                _timer.Start();
                await _notifier.NotifyAsync($"Started {parsedMinutes}-minute focus session for '{targetName}'.", Severity.Info, CancellationToken.None);
            }));

        results.Add(new CommandItem(
            Id: $"quick-set-alert-{parsedMinutes}",
            Title: $"Set alert milestone to {parsedMinutes} minutes",
            Subtitle: "Configure time's up alert milestone without starting timer",
            Category: TimerCategory,
            Icon: Icons.Material.Filled.HourglassTop,
            Action: async () =>
            {
                _close();
                _timer.PomodoroModeEnabled = false;
                _timer.FocusAlertAfter = TimeSpan.FromMinutes(parsedMinutes);
                await _notifier.NotifyAsync($"Focus duration set to {parsedMinutes} minutes.", Severity.Info, CancellationToken.None);
            }));
    }

    private static bool IsStopQuery(string q) =>
        q.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
        q.Equals("log", StringComparison.OrdinalIgnoreCase) ||
        q.Equals("lock", StringComparison.OrdinalIgnoreCase);

    private static bool IsPomodoroQuery(string q) =>
        q.Equals("pomodoro", StringComparison.OrdinalIgnoreCase) ||
        q.Equals("comodoro", StringComparison.OrdinalIgnoreCase) ||
        q.Equals("modraw", StringComparison.OrdinalIgnoreCase) ||
        q.Equals("pomo", StringComparison.OrdinalIgnoreCase);

    private bool HasActiveOrResumableSession() =>
        _timer.IsRunning || HasResumableSession();

    public async Task TogglePomodoroModeAsync()
    {
        _close();
        _timer.TogglePomodoroMode();
        await _notifier.NotifyAsync(
            _timer.PomodoroModeEnabled ? "Pomodoro mode enabled." : "Pomodoro mode disabled. Stopwatch mode enabled.",
            Severity.Info);
    }

    public async Task StopTimerSessionAsync()
    {
        _close();
        var elapsed = _timer.Stop();
        _timer.ClearTargetAndFocus();
        if (elapsed > TimeSpan.Zero)
        {
            if (_timerSessionLog is not null)
            {
                var result = await _timerSessionLog.LogStoppedSessionAsync(elapsed);
                if (result.BoardUpdateFailed || !result.BoardProgressed)
                {
                    await _notifier.NotifyAsync(result.UserMessage, result.BoardUpdateFailed ? Severity.Error : Severity.Success);
                }
            }
            else
            {
                await _notifier.NotifyAsync($"Focus session stopped. Logged {GlobalTimerService.FormatTimeSpan(elapsed)}.", Severity.Success);
            }
            await _notifyRefresh();
        }
        else
        {
            await _notifier.NotifyAsync("Focus session stopped.", Severity.Info);
        }
    }

    public async Task StartPomodoroSessionAsync()
    {
        _close();
        _timer.PomodoroModeEnabled = true;
        if (!_timer.IsRunning)
        {
            _timer.Start();
        }
        var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? GeneralSessionLabel : _timer.TargetId;
        await _notifier.NotifyAsync($"Pomodoro session started for '{targetName}'.", Severity.Info);
    }
    private async Task PromptCustomDurationAndStartAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        _close();
        var parameters = new DialogParameters<SetFocusDurationDialog>
        {
            { x => x.TargetTitle, displayLabel },
            { x => x.InitialDuration, _timer.FocusAlertAfter },
            { x => x.StartSession, true }
        };
        var dialog = await _dialogs.ShowAsync<SetFocusDurationDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
        {
            var duration = result.Data as TimeSpan?;
            ApplyTarget(targetType, targetTitle, boardItemId, displayLabel);
            _timer.PomodoroModeEnabled = false;
            _timer.FocusAlertAfter = duration;
            _timer.Start();
            var durLabel = duration.HasValue ? GlobalTimerService.FormatTimeSpan(duration.Value) : KwContinuous;
            await _notifier.NotifyAsync($"Started {durLabel} focus session for '{displayLabel}'.", Severity.Info);
        }
    }

    private async Task PromptCustomPomodoroDurationAndStartAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        _close();
        var parameters = new DialogParameters<SetFocusDurationDialog>
        {
            { x => x.TargetTitle, $"{displayLabel}, Pomodoro" },
            { x => x.InitialDuration, _timer.WorkDuration },
            { x => x.StartSession, true }
        };
        var dialog = await _dialogs.ShowAsync<SetFocusDurationDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
        {
            var duration = result.Data as TimeSpan?;
            _timer.PomodoroModeEnabled = true;
            if (duration.HasValue && duration.Value > TimeSpan.Zero)
            {
                _timer.WorkDuration = duration.Value;
                _timer.FocusAlertAfter = duration.Value;
            }
            ApplyTarget(targetType, targetTitle, boardItemId, displayLabel);
            _timer.Start();
            var durLabel = duration.HasValue ? GlobalTimerService.FormatTimeSpan(duration.Value) : GlobalTimerService.FormatTimeSpan(_timer.WorkDuration);
            await _notifier.NotifyAsync($"Pomodoro session of {durLabel} work started for '{displayLabel}'.", Severity.Info);
        }
    }

    private async Task PromptCustomTargetAndDurationFlowAsync(bool isPomodoro)
    {
        _close();
        var parameters = new DialogParameters<SetSessionTargetDialog>
        {
            { x => x.CurrentTarget, _timer.TargetId }
        };
        var dialog = await _dialogs.ShowAsync<SetSessionTargetDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled && result.Data is string customTarget && !string.IsNullOrWhiteSpace(customTarget))
        {
            var trimmed = customTarget.Trim();
            if (isPomodoro)
            {
                await PromptCustomPomodoroDurationAndStartAsync(null, null, null, trimmed);
            }
            else
            {
                await PromptCustomDurationAndStartAsync(null, null, null, trimmed);
            }
        }
    }

    private async Task PromptSetCustomTargetAsync()
    {
        _close();
        var parameters = new DialogParameters<SetSessionTargetDialog>
        {
            { x => x.CurrentTarget, _timer.TargetId }
        };
        var dialog = await _dialogs.ShowAsync<SetSessionTargetDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled && result.Data is string customTarget)
        {
            var trimmed = customTarget.Trim();
            _timer.SetManualTarget(string.IsNullOrWhiteSpace(trimmed) ? null : trimmed);
            var label = string.IsNullOrWhiteSpace(trimmed) ? "Cleared session target." : $"Session target set to '{trimmed}'.";
            await _notifier.NotifyAsync(label, Severity.Info);
        }
    }

    private async Task PromptSetCustomDurationAsync()
    {
        _close();
        var parameters = new DialogParameters<SetFocusDurationDialog>
        {
            { x => x.TargetTitle, _timer.TargetId },
            { x => x.InitialDuration, _timer.FocusAlertAfter },
            { x => x.StartSession, false }
        };
        var dialog = await _dialogs.ShowAsync<SetFocusDurationDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
        {
            var duration = result.Data as TimeSpan?;
            _timer.PomodoroModeEnabled = false;
            _timer.FocusAlertAfter = duration;
            var label = duration.HasValue ? GlobalTimerService.FormatTimeSpan(duration.Value) : KwContinuous;
            await _notifier.NotifyAsync($"Focus duration milestone set to {label}.", Severity.Info);
        }
    }
    private Task<List<CommandItem>> GetTimerModeSelectionCommandsAsync()
    {
        return Task.FromResult<List<CommandItem>>([
            new(
                Id: "mode-stopwatch",
                Title: !_timer.PomodoroModeEnabled ? "Stopwatch session, active mode" : "Stopwatch session",
                Subtitle: !_timer.PomodoroModeEnabled
                    ? "Currently active mode. Open count-up focus with optional duration milestone"
                    : "Open count-up focus with optional duration milestone",
                Category: ModeCategory,
                Icon: !_timer.PomodoroModeEnabled ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                ChildrenProvider: () => GetTargetsForModeAsync(isPomodoro: false),
                Keywords: [KwStopwatch, KwTimer, KwOpen, KwContinuous, "count-up"]),

            new(
                Id: "mode-pomodoro",
                Title: _timer.PomodoroModeEnabled ? "Pomodoro session, active mode" : "Pomodoro session",
                Subtitle: _timer.PomodoroModeEnabled
                    ? "Currently active mode. Interval focus with scheduled work and break periods"
                    : "Interval focus with scheduled work and break periods",
                Category: ModeCategory,
                Icon: _timer.PomodoroModeEnabled ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timelapse,
                ChildrenProvider: () => GetTargetsForModeAsync(isPomodoro: true),
                Keywords: [KwPomodoro, KwComodoro, KwModraw, KwInterval, KwWork, KwBreak])
        ]);
    }
    private async Task<List<CommandItem>> GetTargetsForModeAsync(bool isPomodoro)
    {
        var modeName = isPomodoro ? "Pomodoro" : "Stopwatch";
        var targets = new List<CommandItem>
        {
            new(
                Id: "timer-target-empty",
                Title: "Untargeted session",
                Subtitle: $"Start {modeName} session without an item target",
                Category: "Session Target",
                Icon: Icons.Material.Outlined.HourglassEmpty,
                ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, null, null, null, GeneralSessionLabel)),
                Keywords: [KwEmpty, "none", "general", KwOpen, "free", KwSession]),

            new(
                Id: "timer-target-custom",
                Title: "Custom target label...",
                Subtitle: $"Type a custom session label and choose duration for {modeName}",
                Category: "Session Target",
                Icon: Icons.Material.Filled.Edit,
                Action: () => PromptCustomTargetAndDurationFlowAsync(isPomodoro),
                Keywords: [KwCustom, KwTarget, KwLabel, "text", "manual"])
        };

        try
        {
            var snapshot = await _boardData.GetSnapshotAsync();

            foreach (var h in snapshot.Habits)
            {
                targets.Add(new CommandItem(
                    Id: $"timer-target-habit-{h.Id}",
                    Title: h.Title,
                    Subtitle: $"Habit, +{h.Counter}, -{h.NegativeCounter}",
                    Category: "Habits",
                    Icon: Icons.Material.Filled.Repeat,
                    ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, "Habit", h.Title, h.Id, h.Title)),
                    Keywords: [KwHabit, h.Title]));
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var d in snapshot.Dailies)
            {
                var isDone = d.DailyLastCompletedOn.HasValue && d.DailyLastCompletedOn.Value == today;
                var dailySub = isDone ? $"Daily, completed today, streak: {d.Counter}" : $"Daily, streak: {d.Counter}";
                targets.Add(new CommandItem(
                    Id: $"timer-target-daily-{d.Id}",
                    Title: d.Title,
                    Subtitle: dailySub,
                    Category: "Dailies",
                    Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                    ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, "Daily", d.Title, d.Id, d.Title)),
                    Keywords: [KwDaily, d.Title]));
            }

            foreach (var t in snapshot.Todos.Where(todo => !todo.IsCompleted))
            {
                var due = t.TodoDueDate.HasValue ? $"Due {t.TodoDueDate.Value:MMM d}" : "Open";
                targets.Add(new CommandItem(
                    Id: $"timer-target-todo-{t.Id}",
                    Title: t.Title,
                    Subtitle: $"To-do, {due}",
                    Category: "To-dos",
                    Icon: Icons.Material.Filled.CheckBoxOutlineBlank,
                    ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, "Todo", t.Title, t.Id, t.Title)),
                    Keywords: [KwTodo, t.Title]));
            }
        }
        catch
        {
            // Best effort.
        }

        return targets;
    }
    private List<CommandItem> GetDurationsForTargetAndMode(bool isPomodoro, string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        var targetSlug = targetTitle is not null ? targetTitle.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant() : "empty";
        return isPomodoro
            ? GetPomodoroDurations(targetSlug, targetType, targetTitle, boardItemId, displayLabel)
            : GetStopwatchDurations(targetSlug, targetType, targetTitle, boardItemId, displayLabel);
    }

    private List<CommandItem> GetPomodoroDurations(string targetSlug, string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel) =>
    [
        new(
            Id: $"start-{targetSlug}-custom",
            Title: "Custom work duration and start...",
            Subtitle: "Enter custom minutes like 35m and start immediately",
            Category: DurationCategory,
            Icon: Icons.Material.Filled.EditCalendar,
            Action: () => PromptCustomPomodoroDurationAndStartAsync(targetType, targetTitle, boardItemId, displayLabel),
            Keywords: [KwCustom, KwDuration, KwPomodoro, KwMinutes]),
        new(
            Id: $"start-{targetSlug}-pomodoro",
            Title: $"Default interval, {_timer.WorkDuration.TotalMinutes:0}m work",
            Subtitle: $"Currently configured. Start standard {GlobalTimerService.FormatTimeSpan(_timer.WorkDuration)} work session",
            Category: DurationCategory,
            Icon: Icons.Material.Filled.CheckCircle,
            Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, _timer.WorkDuration),
            Keywords: [KwPomodoro, "default", "standard"]),
        BuildPomodoroIntervalCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 15),
        BuildPomodoroIntervalCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 20),
        BuildPomodoroIntervalCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 25),
        BuildPomodoroIntervalCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 30),
        BuildPomodoroIntervalCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 45),
        BuildPomodoroIntervalCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 50),
    ];

    private CommandItem BuildPomodoroIntervalCommand(string targetSlug, string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel, int minutes)
    {
        var workDuration = TimeSpan.FromMinutes(minutes);
        var isCurrent = _timer.WorkDuration == workDuration;
        return new(
            Id: $"start-{targetSlug}-pomo-{minutes}m",
            Title: $"{minutes}-minute work interval",
            Subtitle: isCurrent
                ? $"Currently configured. Start {minutes}-minute Pomodoro work session"
                : $"Start {minutes}-minute Pomodoro work session",
            Category: DurationCategory,
            Icon: isCurrent ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
            Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, workDuration),
            Keywords: [$"{minutes}", $"{minutes}m"]);
    }

    private List<CommandItem> GetStopwatchDurations(string targetSlug, string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        List<CommandItem> list =
        [
            new(
                Id: $"start-{targetSlug}-custom",
                Title: "Custom duration and start...",
                Subtitle: "Enter custom minutes or mm:ss and start immediately",
                Category: DurationCategory,
                Icon: Icons.Material.Filled.EditCalendar,
                Action: () => PromptCustomDurationAndStartAsync(targetType, targetTitle, boardItemId, displayLabel),
                Keywords: [KwCustom, KwDuration, "time", KwMinutes]),
            new(
                Id: $"start-{targetSlug}-open",
                Title: "Continuous count-up without limit",
                Subtitle: _timer.FocusAlertAfter is null
                    ? "Currently selected. Run stopwatch with no time alert"
                    : "Run stopwatch with no time alert",
                Category: DurationCategory,
                Icon: _timer.FocusAlertAfter is null
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.AllInclusive,
                Action: () => StartOpenStopwatchAsync(targetType, targetTitle, boardItemId, displayLabel),
                Keywords: [KwOpen, KwContinuous, KwStopwatch, "unlimited"]),
            BuildStopwatchDurationCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 15, [Kw15, Kw15M, "15 min", "quarter"]),
            BuildStopwatchDurationCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 25, [Kw25, Kw25M, "25 min"]),
            BuildStopwatchDurationCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 30, [Kw30, Kw30M, "30 min", "half hour"]),
            BuildStopwatchDurationCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 45, [Kw45, Kw45M, "45 min"]),
            BuildStopwatchDurationCommand(targetSlug, targetType, targetTitle, boardItemId, displayLabel, 60, ["60", "60m", "60 min", "hour", "1h"]),
        ];
        return list;
    }

    private CommandItem BuildStopwatchDurationCommand(string targetSlug, string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel, int minutes, string[] keywords)
    {
        var duration = TimeSpan.FromMinutes(minutes);
        var isCurrent = _timer.FocusAlertAfter == duration;
        var title = minutes == 60 ? "60 minutes, 1 hour" : $"{minutes} minutes";
        return new(
            Id: $"start-{targetSlug}-{minutes}m",
            Title: title,
            Subtitle: isCurrent
                ? $"Currently selected. Start stopwatch with alert after {minutes} minutes"
                : $"Start stopwatch with alert after {minutes} minutes",
            Category: DurationCategory,
            Icon: isCurrent ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
            Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, duration),
            Keywords: keywords);
    }

    private async Task StartOpenStopwatchAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        _close();
        _timer.PomodoroModeEnabled = false;
        _timer.FocusAlertAfter = null;
        ApplyTarget(targetType, targetTitle, boardItemId, displayLabel);
        _timer.Start();
        await _notifier.NotifyAsync($"Started open stopwatch session for '{displayLabel}'.", Severity.Info);
    }

    private void ApplyTarget(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel)
    {
        if (targetType is not null && targetTitle is not null)
        {
            _timer.SelectTarget(targetType, targetTitle, boardItemId);
        }
        else if (displayLabel != GeneralSessionLabel)
        {
            _timer.SetManualTarget(displayLabel);
        }
        else
        {
            _timer.SetManualTarget(null);
        }
    }

    private async Task StartPomodoroWithWorkDurationAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel, TimeSpan workDuration)
    {
        _close();
        _timer.PomodoroModeEnabled = true;
        _timer.WorkDuration = workDuration;
        _timer.FocusAlertAfter = workDuration;
        ApplyTarget(targetType, targetTitle, boardItemId, displayLabel);
        _timer.Start();
        await _notifier.NotifyAsync($"Pomodoro work session of {GlobalTimerService.FormatTimeSpan(workDuration)} started for '{displayLabel}'.", Severity.Info);
    }

    private async Task StartStopwatchWithDurationAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel, TimeSpan duration)
    {
        _close();
        _timer.PomodoroModeEnabled = false;
        _timer.FocusAlertAfter = duration;
        ApplyTarget(targetType, targetTitle, boardItemId, displayLabel);
        _timer.Start();
        await _notifier.NotifyAsync($"Started {GlobalTimerService.FormatTimeSpan(duration)} focus session for '{displayLabel}'.", Severity.Info);
    }
    private async Task<List<CommandItem>> GetTimerTargetConfigurationCommandsAsync()
    {
        var items = new List<CommandItem>();
        try
        {
            if (_timer.TargetId is not null)
            {
                items.Add(new CommandItem(
                    Id: "target-config-clear",
                    Title: "Clear active target",
                    Subtitle: $"Remove current target '{_timer.TargetId}'",
                    Category: "Target",
                    Icon: Icons.Material.Filled.TimerOff,
                    Action: async () =>
                    {
                        _close();
                        _timer.SetManualTarget(null);
                        await _notifier.NotifyAsync("Timer target cleared.", Severity.Info);
                    },
                    Keywords: [KwClear, "none", KwEmpty, "remove"]));
            }

            items.Add(new CommandItem(
                Id: "target-config-custom",
                Title: "Custom target label...",
                Subtitle: "Type a custom free-text label for this session",
                Category: "Target",
                Icon: Icons.Material.Filled.Edit,
                Action: PromptSetCustomTargetAsync,
                Keywords: [KwCustom, KwTarget, KwLabel, "text", "manual"]));

            var snapshot = await _boardData.GetSnapshotAsync();

            foreach (var h in snapshot.Habits)
            {
                items.Add(new CommandItem(
                    Id: $"target-config-habit-{h.Id}",
                    Title: h.Title,
                    Subtitle: $"Habit, +{h.Counter}, -{h.NegativeCounter}",
                    Category: "Habits",
                    Icon: Icons.Material.Filled.Repeat,
                    Action: async () =>
                    {
                        _close();
                        _timer.SelectTarget("Habit", h.Title, h.Id);
                        await _notifier.NotifyAsync($"Session target set to habit '{h.Title}'.", Severity.Info);
                    },
                    Keywords: [KwHabit, h.Title]));
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var d in snapshot.Dailies)
            {
                var isDone = d.DailyLastCompletedOn.HasValue && d.DailyLastCompletedOn.Value == today;
                var dailySub = isDone ? $"Daily, completed today, streak: {d.Counter}" : $"Daily, streak: {d.Counter}";
                items.Add(new CommandItem(
                    Id: $"target-config-daily-{d.Id}",
                    Title: d.Title,
                    Subtitle: dailySub,
                    Category: "Dailies",
                    Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                    Action: async () =>
                    {
                        _close();
                        _timer.SelectTarget("Daily", d.Title, d.Id);
                        await _notifier.NotifyAsync($"Session target set to daily '{d.Title}'.", Severity.Info);
                    },
                    Keywords: [KwDaily, d.Title]));
            }

            foreach (var t in snapshot.Todos.Where(todo => !todo.IsCompleted))
            {
                var due = t.TodoDueDate.HasValue ? $"Due {t.TodoDueDate.Value:MMM d}" : "Open";
                items.Add(new CommandItem(
                    Id: $"target-config-todo-{t.Id}",
                    Title: t.Title,
                    Subtitle: $"To-do, {due}",
                    Category: "To-dos",
                    Icon: Icons.Material.Filled.CheckBoxOutlineBlank,
                    Action: async () =>
                    {
                        _close();
                        _timer.SelectTarget("Todo", t.Title, t.Id);
                        await _notifier.NotifyAsync($"Session target set to to-do '{t.Title}'.", Severity.Info);
                    },
                    Keywords: [KwTodo, t.Title]));
            }
        }
        catch
        {
            // Best effort.
        }

        return items;
    }
    private Task<List<CommandItem>> GetDurationConfigurationCommandsAsync()
    {
        var targetDisplay = string.IsNullOrWhiteSpace(_timer.TargetId) ? GeneralSessionLabel : _timer.TargetId;
        List<CommandItem> list =
        [
            new(
                Id: "duration-config-custom",
                Title: "Custom duration and start...",
                Subtitle: $"Enter custom duration and immediately start session for '{targetDisplay}'",
                Category: DurationCategory,
                Icon: Icons.Material.Filled.EditCalendar,
                Action: () => PromptCustomDurationAndStartAsync(_timer.TargetType, _timer.TargetId, _timer.BoardItemId, targetDisplay),
                Keywords: [KwCustom, KwDuration, KwStart, KwMinutes]),
            new(
                Id: "duration-config-custom-milestone",
                Title: "Set custom duration milestone...",
                Subtitle: "Set time's up alert milestone without starting timer",
                Category: DurationCategory,
                Icon: Icons.Material.Filled.HourglassTop,
                Action: PromptSetCustomDurationAsync,
                Keywords: [KwCustom, KwDuration, "alert", "milestone"]),
            new(
                Id: "duration-config-open",
                Title: "Continuous count-up without alert",
                Subtitle: _timer.FocusAlertAfter is null
                    ? "Currently selected. Stopwatch will run until manually paused or stopped"
                    : "Stopwatch will run until manually paused or stopped",
                Category: DurationCategory,
                Icon: _timer.FocusAlertAfter is null ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.AllInclusive,
                Action: async () =>
                {
                    _close();
                    _timer.FocusAlertAfter = null;
                    await _notifier.NotifyAsync("Focus duration cleared for continuous session.", Severity.Info);
                },
                Keywords: [KwOpen, KwContinuous, KwClear, KwStopwatch]),
            BuildDurationMilestoneCommand(15, "15 minutes", [Kw15, Kw15M, "quarter"]),
            BuildDurationMilestoneCommand(25, "25 minutes", [Kw25, Kw25M]),
            BuildDurationMilestoneCommand(30, "30 minutes", [Kw30, Kw30M, "half hour"]),
            BuildDurationMilestoneCommand(45, "45 minutes", [Kw45, Kw45M]),
            BuildDurationMilestoneCommand(60, "60 minutes, 1 hour", ["60", "60m", "hour", "1h"]),
            BuildDurationMilestoneCommand(90, "90 minutes, 1.5 hours", ["90", "90m", "1.5h"]),
        ];

        if (_timer.PomodoroModeEnabled)
        {
            list.Insert(0, new(
                Id: "duration-pomodoro-info",
                Title: $"Pomodoro interval: {GlobalTimerService.FormatTimeSpan(_timer.WorkDuration)} work and {GlobalTimerService.FormatTimeSpan(_timer.ShortBreakDuration)} break",
                Subtitle: "Configured in Settings. Setting a custom duration above switches to Stopwatch mode.",
                Category: DurationCategory,
                Icon: Icons.Material.Filled.Settings,
                Action: () => _navigate("/settings"),
                Keywords: [KwPomodoro, "settings", KwInterval, KwDuration]));
        }

        return Task.FromResult(list);
    }

    private CommandItem BuildDurationMilestoneCommand(int minutes, string title, string[] keywords)
    {
        var duration = TimeSpan.FromMinutes(minutes);
        var isCurrent = _timer.FocusAlertAfter == duration;
        return new(
            Id: $"duration-config-{minutes}m",
            Title: title,
            Subtitle: isCurrent
                ? $"Currently selected. Set time's up alert milestone to {minutes} minutes"
                : $"Set time's up alert milestone to {minutes} minutes",
            Category: DurationCategory,
            Icon: isCurrent ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
            Action: () => SetDurationMilestoneAsync(duration, minutes),
            Keywords: keywords);
    }

    private async Task SetDurationMilestoneAsync(TimeSpan duration, int minutes)
    {
        _close();
        _timer.FocusAlertAfter = duration;
        await _notifier.NotifyAsync($"Focus duration set to {minutes} minutes.", Severity.Info);
    }
}
#pragma warning restore S107
