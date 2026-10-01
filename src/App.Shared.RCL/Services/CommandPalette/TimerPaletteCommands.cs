using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;

using MudBlazor;

namespace App.Shared.RCL.Services.CommandPalette;

/// <summary>Focus timer catalog, search shortcuts, sessions, and the start wizard.</summary>
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

    public void AddSuggestedTimerCommands(List<CommandItem> list)
    {
        // Timer action based on current state
        if (_timer.IsRunning)
        {
            list.Add(new(
                Id: "timer-stop",
                Title: "Stop focus session",
                Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Stop,
                ShortcutBadge: "S",
                Action: StopTimerSessionAsync,
                Keywords: ["timer", "stop", "session", "virtual session", "focus", "log", "lock", "end"]));

            list.Add(new(
                Id: "timer-pause",
                Title: "Pause focus timer",
                Subtitle: _timer.PomodoroModeEnabled ? $"Pause active {_timer.StatusLabel}" : "Temporarily pause the running session",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Pause,
                Action: () =>
                {
                    _close();
                    _timer.Pause();
                    return Task.CompletedTask;
                },
                Keywords: ["timer", "pause", "session", "virtual session", "focus", "break"]));

            list.Add(new(
                Id: "timer-reset",
                Title: "Reset focus timer",
                Subtitle: _timer.PomodoroModeEnabled ? "Reset Pomodoro intervals and return to idle" : "Reset session timer to zero",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Refresh,
                Action: async () =>
                {
                    _close();
                    _timer.ResetSession();
                    await _notifier.NotifyAsync(_timer.PomodoroModeEnabled ? "Pomodoro session reset to idle." : "Stopwatch timer reset to zero.", Severity.Info);
                },
                Keywords: ["timer", "reset", "clear", "zero"]));

            if (_timer.PomodoroModeEnabled && (_timer.CurrentPomodoroState == PomodoroState.ShortBreak || _timer.CurrentPomodoroState == PomodoroState.LongBreak))
            {
                list.Add(new(
                    Id: "timer-skip-break",
                    Title: "Skip break and resume work",
                    Subtitle: "End break early and start next Pomodoro work interval",
                    Category: "Suggested",
                    Icon: Icons.Material.Filled.SkipNext,
                    Action: async () =>
                    {
                        _close();
                        _timer.TransitionToWork();
                        _timer.Start();
                        var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
                        await _notifier.NotifyAsync($"Break skipped. Work started for '{targetName}'.", Severity.Info);
                    },
                    Keywords: ["skip", "break", "work", "pomodoro"]));
            }
        }
        else if (_timer.PomodoroModeEnabled && (_timer.CurrentPomodoroState == PomodoroState.ShortBreak || _timer.CurrentPomodoroState == PomodoroState.LongBreak))
        {
            list.Add(new(
                Id: "timer-start-break",
                Title: "Start break countdown",
                Subtitle: $"Begin {_timer.StatusLabel}, {GlobalTimerService.FormatTimeSpan(_timer.FocusAlertAfter ?? _timer.ShortBreakDuration)}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Coffee,
                ShortcutBadge: "S",
                Action: () =>
                {
                    _close();
                    _timer.Start();
                    return Task.CompletedTask;
                },
                Keywords: ["break", "start", "timer", "coffee", "rest"]));

            list.Add(new(
                Id: "timer-skip-break",
                Title: "Skip break and start work",
                Subtitle: "Skip break and start next Pomodoro work interval immediately",
                Category: "Suggested",
                Icon: Icons.Material.Filled.SkipNext,
                Action: async () =>
                {
                    _close();
                    _timer.TransitionToWork();
                    _timer.Start();
                    var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
                    await _notifier.NotifyAsync($"Break skipped. Work started for '{targetName}'.", Severity.Info);
                },
                Keywords: ["skip", "break", "work", "pomodoro"]));

            list.Add(new(
                Id: "timer-stop",
                Title: "Stop focus session",
                Subtitle: "Reset and end the active Pomodoro cycle",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Stop,
                Action: StopTimerSessionAsync,
                Keywords: ["timer", "stop", "session", "virtual session", "end", "log", "lock"]));

            list.Add(new(
                Id: "timer-reset",
                Title: "Reset focus timer",
                Subtitle: "Reset Pomodoro intervals and return to idle",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Refresh,
                Action: async () =>
                {
                    _close();
                    _timer.ResetSession();
                    await _notifier.NotifyAsync("Pomodoro session reset to idle.", Severity.Info);
                },
                Keywords: ["timer", "reset", "clear", "zero"]));
        }
        else if (_timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle))
        {
            var resumeLabel = _timer.PomodoroModeEnabled
                ? $"Resume Pomodoro, {_timer.GetDisplayTime()} remaining"
                : $"Resume focus session, {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} elapsed";

            list.Add(new(
                Id: "timer-resume",
                Title: "Resume focus session",
                Subtitle: resumeLabel,
                Category: "Suggested",
                Icon: Icons.Material.Filled.PlayArrow,
                ShortcutBadge: "S",
                Action: () =>
                {
                    _close();
                    _timer.Start();
                    return Task.CompletedTask;
                },
                Keywords: ["timer", "resume", "start", "focus", "session", "virtual session"]));

            list.Add(new(
                Id: "timer-stop",
                Title: "Stop focus session",
                Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Stop,
                Action: StopTimerSessionAsync,
                Keywords: ["timer", "stop", "session", "virtual session", "focus", "log", "lock", "end"]));

            list.Add(new(
                Id: "timer-reset",
                Title: "Reset focus timer",
                Subtitle: _timer.PomodoroModeEnabled ? "Reset Pomodoro intervals and return to idle" : "Reset session timer to zero",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Refresh,
                Action: async () =>
                {
                    _close();
                    _timer.ResetSession();
                    await _notifier.NotifyAsync(_timer.PomodoroModeEnabled ? "Pomodoro session reset to idle." : "Stopwatch timer reset to zero.", Severity.Info);
                },
                Keywords: ["timer", "reset", "clear", "zero"]));
        }
        else
        {
            var targetDesc = string.IsNullOrWhiteSpace(_timer.TargetId)
                ? "empty session"
                : $"target: {_timer.TargetId}";

            list.Add(new(
                Id: "timer-setup-and-start",
                Title: "Start focus session...",
                Subtitle: "Choose Stopwatch or Pomodoro, then select target and duration",
                Category: "Suggested",
                Icon: Icons.Material.Filled.PlayCircleOutline,
                ChildrenProvider: GetTimerModeSelectionCommandsAsync,
                Keywords: ["timer", "session", "virtual session", "start", "target", "focus", "duration", "explore", "empty", "modraw", "pomodoro", "comodoro", "stopwatch"]));

            list.Add(new(
                Id: "timer-start-pomodoro",
                Title: "Quick start Pomodoro",
                Subtitle: $"Start {_timer.WorkDuration.TotalMinutes:0}m Pomodoro work interval, {targetDesc}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.Timelapse,
                ShortcutBadge: "P",
                Action: StartPomodoroSessionAsync,
                Keywords: ["pomodoro", "comodoro", "modraw", "quick start pomodoro", "start", "focus", "virtual session"]));

            list.Add(new(
                Id: "timer-start",
                Title: "Start Stopwatch session",
                Subtitle: $"Start open stopwatch focus session, {targetDesc}",
                Category: "Suggested",
                Icon: Icons.Material.Filled.PlayArrow,
                ShortcutBadge: "S",
                Action: async () =>
                {
                    _close();
                    _timer.PomodoroModeEnabled = false;
                    _timer.Start();
                    var tName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
                    await _notifier.NotifyAsync($"Stopwatch session started for '{tName}'.", Severity.Info);
                },
                Keywords: ["timer", "start", "stopwatch", "open", "focus", "session"]));
        }
    }

    public void AddTimerActionCommands(List<CommandItem> list)
    {
        if (_timer.IsRunning || _timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle))
        {
            list.Add(new(
                Id: "timer-setup-and-start",
                Title: "Start new focus session...",
                Subtitle: "Choose Stopwatch or Pomodoro mode, then select target and duration",
                Category: "Actions",
                Icon: Icons.Material.Filled.PlayCircleOutline,
                ChildrenProvider: GetTimerModeSelectionCommandsAsync,
                Keywords: ["timer", "session", "virtual session", "start", "target", "focus", "duration", "explore", "empty", "modraw", "pomodoro", "comodoro", "stopwatch", "habit", "daily", "todo"]));
        }

        var targetDisplay = string.IsNullOrWhiteSpace(_timer.TargetId) ? "None" : _timer.TargetId;
        list.Add(new(
            Id: "timer-set-target",
            Title: "Set session target...",
            Subtitle: $"Target: {targetDisplay} - Assign habit, daily, to-do, or custom label",
            Category: "Actions",
            Icon: Icons.Material.Filled.AdsClick,
            ChildrenProvider: GetTimerTargetConfigurationCommandsAsync,
            Keywords: ["timer", "target", "focus", "item", "habit", "daily", "todo", "custom target", "label", "set target"]));

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
            Category: "Actions",
            Icon: Icons.Material.Filled.HourglassTop,
            ChildrenProvider: GetDurationConfigurationCommandsAsync,
            Keywords: ["timer", "duration", "alert", "time's up", "focus", "minutes", "length", "custom duration"]));

        list.Add(new(
            Id: "timer-reset",
            Title: "Reset focus timer",
            Subtitle: _timer.PomodoroModeEnabled ? "Reset Pomodoro intervals and return to idle" : "Reset session timer to zero",
            Category: "Actions",
            Icon: Icons.Material.Filled.Refresh,
            Action: async () =>
            {
                _close();
                _timer.ResetSession();
                await _notifier.NotifyAsync("Focus timer reset.", Severity.Info);
            },
            Keywords: ["timer", "reset", "clear"]));

        if (_timer.TargetId is not null)
        {
            list.Add(new(
                Id: "timer-clear-target",
                Title: "Clear timer target",
                Subtitle: $"Active target: {_timer.TargetId}",
                Category: "Actions",
                Icon: Icons.Material.Filled.TimerOff,
                Action: async () =>
                {
                    _close();
                    _timer.SetManualTarget(null);
                    await _notifier.NotifyAsync("Timer target cleared.", Severity.Info);
                },
                Keywords: ["timer", "clear target", "target", "stop"]));
        }

        list.Add(new(
            Id: "timer-toggle-pomodoro",
            Title: _timer.PomodoroModeEnabled ? "Disable Pomodoro mode" : "Enable Pomodoro mode",
            Subtitle: _timer.PomodoroModeEnabled ? "Switch to open Stopwatch focus mode" : "Switch to interval focus sessions with scheduled breaks",
            Category: "Actions",
            Icon: Icons.Material.Filled.Timelapse,
            Action: TogglePomodoroModeAsync,
            Keywords: ["pomodoro", "modraw", "timer", "interval", "break", "virtual session", "mode"]));
    }

    public void AddTimerShortcutCommands(string q, List<CommandItem> results)
    {
        // Direct timer controls: resume, pause, stop, log, and reset
        if (q.Equals("resume", StringComparison.OrdinalIgnoreCase) && !_timer.IsRunning &&
            (_timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle)))
        {
            var resumeLabel = _timer.PomodoroModeEnabled
                ? $"Resume Pomodoro, {_timer.GetDisplayTime()} remaining"
                : $"Resume focus session, {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} elapsed";
            results.Add(new CommandItem(
                Id: "timer-resume-search",
                Title: "Resume focus session",
                Subtitle: resumeLabel,
                Category: "Timer",
                Icon: Icons.Material.Filled.PlayArrow,
                Action: () =>
                {
                    _close();
                    _timer.Start();
                    return Task.CompletedTask;
                }));
        }

        if (q.Equals("pause", StringComparison.OrdinalIgnoreCase) && _timer.IsRunning)
        {
            results.Add(new CommandItem(
                Id: "timer-pause-search",
                Title: "Pause focus timer",
                Subtitle: _timer.PomodoroModeEnabled ? $"Pause active {_timer.StatusLabel}" : "Temporarily pause the running session",
                Category: "Timer",
                Icon: Icons.Material.Filled.Pause,
                Action: () =>
                {
                    _close();
                    _timer.Pause();
                    return Task.CompletedTask;
                }));
        }

        if ((q.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
             q.Equals("log", StringComparison.OrdinalIgnoreCase) ||
             q.Equals("lock", StringComparison.OrdinalIgnoreCase)) &&
            (_timer.IsRunning || _timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle)))
        {
            results.Add(new CommandItem(
                Id: "timer-stop-search",
                Title: "Stop and log focus session",
                Subtitle: $"Stop and log session of {GlobalTimerService.FormatTimeSpan(_timer.Elapsed)} to target",
                Category: "Timer",
                Icon: Icons.Material.Filled.Stop,
                Action: StopTimerSessionAsync));
        }

        if (q.Equals("reset", StringComparison.OrdinalIgnoreCase) &&
            (_timer.IsRunning || _timer.Elapsed > TimeSpan.Zero || (_timer.PomodoroModeEnabled && _timer.CurrentPomodoroState != PomodoroState.Idle)))
        {
            results.Add(new CommandItem(
                Id: "timer-reset-search",
                Title: "Reset focus timer",
                Subtitle: _timer.PomodoroModeEnabled ? "Reset Pomodoro intervals and return to idle" : "Reset session timer to zero",
                Category: "Timer",
                Icon: Icons.Material.Filled.Refresh,
                Action: async () =>
                {
                    _close();
                    _timer.ResetSession();
                    await _notifier.NotifyAsync(_timer.PomodoroModeEnabled ? "Pomodoro session reset to idle." : "Stopwatch timer reset to zero.", Severity.Info, CancellationToken.None);
                }));
        }

        // Direct Pomodoro quick-trigger
        if (q.Equals("pomodoro", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("comodoro", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("modraw", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("pomo", StringComparison.OrdinalIgnoreCase))
        {
            var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
            results.Add(new CommandItem(
                Id: "quick-start-pomodoro-search",
                Title: "Start Pomodoro session",
                Subtitle: $"Start {_timer.WorkDuration.TotalMinutes:0}m work interval for {targetName}",
                Category: "Timer",
                Icon: Icons.Material.Filled.Timelapse,
                Action: StartPomodoroSessionAsync));
        }

        // Direct duration intent like "timer 25", "focus 45m", "session 30", or "start 25"
        var durationMatch = System.Text.RegularExpressions.Regex.Match(
            q,
            @"^(?:timer|focus|start|session)\s+(\d{1,3})(?:m|min)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));
        if (durationMatch.Success && int.TryParse(durationMatch.Groups[1].Value, out var parsedMinutes) && parsedMinutes > 0)
        {
            var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
            results.Add(new CommandItem(
                Id: $"quick-start-duration-{parsedMinutes}",
                Title: $"Start {parsedMinutes}-minute focus session",
                Subtitle: $"Start focus timer immediately with alert after {parsedMinutes} minutes for '{targetName}'",
                Category: "Timer",
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
                Category: "Timer",
                Icon: Icons.Material.Filled.HourglassTop,
                Action: async () =>
                {
                    _close();
                    _timer.PomodoroModeEnabled = false;
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(parsedMinutes);
                    await _notifier.NotifyAsync($"Focus duration set to {parsedMinutes} minutes.", Severity.Info, CancellationToken.None);
                }));
        }
    }

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
        var targetName = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
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
            if (targetType is not null && targetTitle is not null)
            {
                _timer.SelectTarget(targetType, targetTitle, boardItemId);
            }
            else if (displayLabel != "General Session")
            {
                _timer.SetManualTarget(displayLabel);
            }
            else
            {
                _timer.SetManualTarget(null);
            }
            _timer.PomodoroModeEnabled = false;
            _timer.FocusAlertAfter = duration;
            _timer.Start();
            var durLabel = duration.HasValue ? GlobalTimerService.FormatTimeSpan(duration.Value) : "continuous";
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
            if (targetType is not null && targetTitle is not null)
            {
                _timer.SelectTarget(targetType, targetTitle, boardItemId);
            }
            else if (displayLabel != "General Session")
            {
                _timer.SetManualTarget(displayLabel);
            }
            else
            {
                _timer.SetManualTarget(null);
            }
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
            var label = duration.HasValue ? GlobalTimerService.FormatTimeSpan(duration.Value) : "continuous";
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
                Category: "Mode",
                Icon: !_timer.PomodoroModeEnabled ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                ChildrenProvider: () => GetTargetsForModeAsync(isPomodoro: false),
                Keywords: ["stopwatch", "timer", "open", "continuous", "count-up"]),

            new(
                Id: "mode-pomodoro",
                Title: _timer.PomodoroModeEnabled ? "Pomodoro session, active mode" : "Pomodoro session",
                Subtitle: _timer.PomodoroModeEnabled
                    ? "Currently active mode. Interval focus with scheduled work and break periods"
                    : "Interval focus with scheduled work and break periods",
                Category: "Mode",
                Icon: _timer.PomodoroModeEnabled ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timelapse,
                ChildrenProvider: () => GetTargetsForModeAsync(isPomodoro: true),
                Keywords: ["pomodoro", "comodoro", "modraw", "interval", "work", "break"])
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
                ChildrenProvider: () => Task.FromResult(GetDurationsForTargetAndMode(isPomodoro, null, null, null, "General Session")),
                Keywords: ["empty", "none", "general", "open", "free", "session"]),

            new(
                Id: "timer-target-custom",
                Title: "Custom target label...",
                Subtitle: $"Type a custom session label and choose duration for {modeName}",
                Category: "Session Target",
                Icon: Icons.Material.Filled.Edit,
                Action: () => PromptCustomTargetAndDurationFlowAsync(isPomodoro),
                Keywords: ["custom", "target", "label", "text", "manual"])
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
                    Keywords: ["habit", h.Title]));
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
                    Keywords: ["daily", d.Title]));
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
                    Keywords: ["todo", t.Title]));
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

        if (isPomodoro)
        {
            return
            [
                new(
                    Id: $"start-{targetSlug}-custom",
                    Title: "Custom work duration and start...",
                    Subtitle: "Enter custom minutes like 35m and start immediately",
                    Category: "Duration",
                    Icon: Icons.Material.Filled.EditCalendar,
                    Action: () => PromptCustomPomodoroDurationAndStartAsync(targetType, targetTitle, boardItemId, displayLabel),
                    Keywords: ["custom", "duration", "pomodoro", "minutes"]),

                new(
                    Id: $"start-{targetSlug}-pomodoro",
                    Title: $"Default interval, {_timer.WorkDuration.TotalMinutes:0}m work",
                    Subtitle: isPomodoro
                        ? $"Currently configured. Start standard {GlobalTimerService.FormatTimeSpan(_timer.WorkDuration)} work session"
                        : "Start standard Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.PlayArrow,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, _timer.WorkDuration),
                    Keywords: ["pomodoro", "default", "standard"]),

                new(
                    Id: $"start-{targetSlug}-pomo-15m",
                    Title: "15-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(15)
                        ? "Currently configured. Start 15-minute Pomodoro work session"
                        : "Start 15-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(15)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(15)),
                    Keywords: ["15", "15m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-20m",
                    Title: "20-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(20)
                        ? "Currently configured. Start 20-minute Pomodoro work session"
                        : "Start 20-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(20)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(20)),
                    Keywords: ["20", "20m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-25m",
                    Title: "25-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(25)
                        ? "Currently configured. Start 25-minute Pomodoro work session"
                        : "Start 25-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(25)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(25)),
                    Keywords: ["25", "25m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-30m",
                    Title: "30-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(30)
                        ? "Currently configured. Start 30-minute Pomodoro work session"
                        : "Start 30-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(30)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(30)),
                    Keywords: ["30", "30m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-45m",
                    Title: "45-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(45)
                        ? "Currently configured. Start 45-minute Pomodoro work session"
                        : "Start 45-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(45)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(45)),
                    Keywords: ["45", "45m"]),

                new(
                    Id: $"start-{targetSlug}-pomo-50m",
                    Title: "50-minute work interval",
                    Subtitle: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(50)
                        ? "Currently configured. Start 50-minute Pomodoro work session"
                        : "Start 50-minute Pomodoro work session",
                    Category: "Duration",
                    Icon: isPomodoro && _timer.WorkDuration == TimeSpan.FromMinutes(50)
                        ? Icons.Material.Filled.CheckCircle
                        : Icons.Material.Filled.Timer,
                    Action: () => StartPomodoroWithWorkDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(50)),
                    Keywords: ["50", "50m"])
            ];
        }

        return
        [
            new(
                Id: $"start-{targetSlug}-custom",
                Title: "Custom duration and start...",
                Subtitle: "Enter custom minutes or mm:ss and start immediately",
                Category: "Duration",
                Icon: Icons.Material.Filled.EditCalendar,
                Action: () => PromptCustomDurationAndStartAsync(targetType, targetTitle, boardItemId, displayLabel),
                Keywords: ["custom", "duration", "time", "minutes"]),

            new(
                Id: $"start-{targetSlug}-open",
                Title: "Continuous count-up without limit",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter is null
                    ? "Currently selected. Run stopwatch with no time alert"
                    : "Run stopwatch with no time alert",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter is null
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.AllInclusive,
                Action: async () =>
                {
                    _close();
                    _timer.PomodoroModeEnabled = false;
                    _timer.FocusAlertAfter = null;
                    if (targetType is not null && targetTitle is not null)
                    {
                        _timer.SelectTarget(targetType, targetTitle, boardItemId);
                    }
                    else if (displayLabel != "General Session")
                    {
                        _timer.SetManualTarget(displayLabel);
                    }
                    else
                    {
                        _timer.SetManualTarget(null);
                    }
                    _timer.Start();
                    await _notifier.NotifyAsync($"Started open stopwatch session for '{displayLabel}'.", Severity.Info);
                },
                Keywords: ["open", "continuous", "stopwatch", "unlimited"]),

            new(
                Id: $"start-{targetSlug}-15m",
                Title: "15 minutes",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(15)
                    ? "Currently selected. Start stopwatch with alert after 15 minutes"
                    : "Start stopwatch with alert after 15 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(15)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(15)),
                Keywords: ["15", "15m", "15 min", "quarter"]),

            new(
                Id: $"start-{targetSlug}-25m",
                Title: "25 minutes",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(25)
                    ? "Currently selected. Start stopwatch with alert after 25 minutes"
                    : "Start stopwatch with alert after 25 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(25)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(25)),
                Keywords: ["25", "25m", "25 min"]),

            new(
                Id: $"start-{targetSlug}-30m",
                Title: "30 minutes",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(30)
                    ? "Currently selected. Start stopwatch with alert after 30 minutes"
                    : "Start stopwatch with alert after 30 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(30)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(30)),
                Keywords: ["30", "30m", "30 min", "half hour"]),

            new(
                Id: $"start-{targetSlug}-45m",
                Title: "45 minutes",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(45)
                    ? "Currently selected. Start stopwatch with alert after 45 minutes"
                    : "Start stopwatch with alert after 45 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(45)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(45)),
                Keywords: ["45", "45m", "45 min"]),

            new(
                Id: $"start-{targetSlug}-60m",
                Title: "60 minutes, 1 hour",
                Subtitle: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(60)
                    ? "Currently selected. Start stopwatch with alert after 60 minutes"
                    : "Start stopwatch with alert after 60 minutes",
                Category: "Duration",
                Icon: !isPomodoro && _timer.FocusAlertAfter == TimeSpan.FromMinutes(60)
                    ? Icons.Material.Filled.CheckCircle
                    : Icons.Material.Filled.Timer,
                Action: () => StartStopwatchWithDurationAsync(targetType, targetTitle, boardItemId, displayLabel, TimeSpan.FromMinutes(60)),
                Keywords: ["60", "60m", "60 min", "hour", "1h"])
        ];
    }
    private async Task StartPomodoroWithWorkDurationAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel, TimeSpan workDuration)
    {
        _close();
        _timer.PomodoroModeEnabled = true;
        _timer.WorkDuration = workDuration;
        _timer.FocusAlertAfter = workDuration;
        if (targetType is not null && targetTitle is not null)
        {
            _timer.SelectTarget(targetType, targetTitle, boardItemId);
        }
        else if (displayLabel != "General Session")
        {
            _timer.SetManualTarget(displayLabel);
        }
        else
        {
            _timer.SetManualTarget(null);
        }
        _timer.Start();
        await _notifier.NotifyAsync($"Pomodoro work session of {GlobalTimerService.FormatTimeSpan(workDuration)} started for '{displayLabel}'.", Severity.Info);
    }

    private async Task StartStopwatchWithDurationAsync(string? targetType, string? targetTitle, Guid? boardItemId, string displayLabel, TimeSpan duration)
    {
        _close();
        _timer.PomodoroModeEnabled = false;
        _timer.FocusAlertAfter = duration;
        if (targetType is not null && targetTitle is not null)
        {
            _timer.SelectTarget(targetType, targetTitle, boardItemId);
        }
        else if (displayLabel != "General Session")
        {
            _timer.SetManualTarget(displayLabel);
        }
        else
        {
            _timer.SetManualTarget(null);
        }
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
                    Keywords: ["clear", "none", "empty", "remove"]));
            }

            items.Add(new CommandItem(
                Id: "target-config-custom",
                Title: "Custom target label...",
                Subtitle: "Type a custom free-text label for this session",
                Category: "Target",
                Icon: Icons.Material.Filled.Edit,
                Action: PromptSetCustomTargetAsync,
                Keywords: ["custom", "target", "label", "text", "manual"]));

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
                    Keywords: ["habit", h.Title]));
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
                    Keywords: ["daily", d.Title]));
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
                    Keywords: ["todo", t.Title]));
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
        var targetDisplay = string.IsNullOrWhiteSpace(_timer.TargetId) ? "General Session" : _timer.TargetId;
        var list = new List<CommandItem>
        {
            new(
                Id: "duration-config-custom",
                Title: "Custom duration and start...",
                Subtitle: $"Enter custom duration and immediately start session for '{targetDisplay}'",
                Category: "Duration",
                Icon: Icons.Material.Filled.EditCalendar,
                Action: () => PromptCustomDurationAndStartAsync(_timer.TargetType, _timer.TargetId, _timer.BoardItemId, targetDisplay),
                Keywords: ["custom", "duration", "start", "minutes"]),

            new(
                Id: "duration-config-custom-milestone",
                Title: "Set custom duration milestone...",
                Subtitle: "Set time's up alert milestone without starting timer",
                Category: "Duration",
                Icon: Icons.Material.Filled.HourglassTop,
                Action: PromptSetCustomDurationAsync,
                Keywords: ["custom", "duration", "alert", "milestone"]),

            new(
                Id: "duration-config-open",
                Title: "Continuous count-up without alert",
                Subtitle: _timer.FocusAlertAfter is null
                    ? "Currently selected. Stopwatch will run until manually paused or stopped"
                    : "Stopwatch will run until manually paused or stopped",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter is null ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.AllInclusive,
                Action: async () =>
                {
                    _close();
                    _timer.FocusAlertAfter = null;
                    await _notifier.NotifyAsync("Focus duration cleared for continuous session.", Severity.Info);
                },
                Keywords: ["open", "continuous", "clear", "stopwatch"]),

            new(
                Id: "duration-config-15m",
                Title: "15 minutes",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(15)
                    ? "Currently selected. Set time's up alert milestone to 15 minutes"
                    : "Set time's up alert milestone to 15 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(15) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    _close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(15);
                    await _notifier.NotifyAsync("Focus duration set to 15 minutes.", Severity.Info);
                },
                Keywords: ["15", "15m", "quarter"]),

            new(
                Id: "duration-config-25m",
                Title: "25 minutes",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(25)
                    ? "Currently selected. Set time's up alert milestone to 25 minutes"
                    : "Set time's up alert milestone to 25 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(25) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    _close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(25);
                    await _notifier.NotifyAsync("Focus duration set to 25 minutes.", Severity.Info);
                },
                Keywords: ["25", "25m"]),

            new(
                Id: "duration-config-30m",
                Title: "30 minutes",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(30)
                    ? "Currently selected. Set time's up alert milestone to 30 minutes"
                    : "Set time's up alert milestone to 30 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(30) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    _close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(30);
                    await _notifier.NotifyAsync("Focus duration set to 30 minutes.", Severity.Info);
                },
                Keywords: ["30", "30m", "half hour"]),

            new(
                Id: "duration-config-45m",
                Title: "45 minutes",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(45)
                    ? "Currently selected. Set time's up alert milestone to 45 minutes"
                    : "Set time's up alert milestone to 45 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(45) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    _close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(45);
                    await _notifier.NotifyAsync("Focus duration set to 45 minutes.", Severity.Info);
                },
                Keywords: ["45", "45m"]),

            new(
                Id: "duration-config-60m",
                Title: "60 minutes, 1 hour",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(60)
                    ? "Currently selected. Set time's up alert milestone to 60 minutes"
                    : "Set time's up alert milestone to 60 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(60) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    _close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(60);
                    await _notifier.NotifyAsync("Focus duration set to 60 minutes.", Severity.Info);
                },
                Keywords: ["60", "60m", "hour", "1h"]),

            new(
                Id: "duration-config-90m",
                Title: "90 minutes, 1.5 hours",
                Subtitle: _timer.FocusAlertAfter == TimeSpan.FromMinutes(90)
                    ? "Currently selected. Set time's up alert milestone to 90 minutes"
                    : "Set time's up alert milestone to 90 minutes",
                Category: "Duration",
                Icon: _timer.FocusAlertAfter == TimeSpan.FromMinutes(90) ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Timer,
                Action: async () =>
                {
                    _close();
                    _timer.FocusAlertAfter = TimeSpan.FromMinutes(90);
                    await _notifier.NotifyAsync("Focus duration set to 90 minutes.", Severity.Info);
                },
                Keywords: ["90", "90m", "1.5h"])
        };

        if (_timer.PomodoroModeEnabled)
        {
            list.Insert(0, new(
                Id: "duration-pomodoro-info",
                Title: $"Pomodoro interval: {GlobalTimerService.FormatTimeSpan(_timer.WorkDuration)} work and {GlobalTimerService.FormatTimeSpan(_timer.ShortBreakDuration)} break",
                Subtitle: "Configured in Settings. Setting a custom duration above switches to Stopwatch mode.",
                Category: "Duration",
                Icon: Icons.Material.Filled.Settings,
                Action: () => _navigate("/settings"),
                Keywords: ["pomodoro", "settings", "interval", "duration"]));
        }

        return Task.FromResult(list);
    }
}
