using System.Globalization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class UserPreferencesSection
{
    [Parameter]
    public bool ShowTitle { get; set; } = true;

    private UserPreferences? _model;
    private bool _loading = true;
    private string? _loadError;
    private TimeSpan? _dayStart;
    private string _displayName = string.Empty;
    private string _datePreview = string.Empty;
    private string _timeZoneSelection = "Auto";
    private string _detectedTimeZoneLabel = "";
    private string _resolvedTimeZoneLabel = "";
    private const string AutoTimeZoneValue = "Auto";
    private string? _displayNameError;
    private string? _dateFormatError;
    private string? _dayStartError;
    private bool _useSystemTheme = true;
    private string _pinnedThemeBtnLabel = "Light mode";
    private Variant _pinnedThemeBtnVariant = Variant.Outlined;
    private string _themeStatusText = string.Empty;

    private readonly List<(string Id, string Label)> _timeZones = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var loaded = await PreferencesService.GetAsync();
            ApplyModel(loaded);
            ApplyTimeZoneOverride();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            try { await Notifier.NotifyAsync("Could not load preferences. Please try again.", Severity.Error); } catch (Exception) { /* Best-effort toast. Fallback UI already indicates the failure. */ }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task RetryLoadAsync()
    {
        _loading = true;
        _loadError = null;
        StateHasChanged();
        try
        {
            var loaded = await PreferencesService.GetAsync();
            ApplyModel(loaded);
            ApplyTimeZoneOverride();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            try { await Notifier.NotifyAsync("Could not load preferences. Please try again.", Severity.Error); } catch (Exception) { /* Best-effort toast. Fallback UI already indicates the failure. */ }
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyModel(UserPreferences preferences)
    {
        _model = Clone(preferences);
        _dayStart = _model.DayStartLocalTime;
        _displayName = _model.DisplayName ?? string.Empty;
        _timeZoneSelection = string.IsNullOrWhiteSpace(_model.TimeZoneOverrideId)
            ? AutoTimeZoneValue
            : _model.TimeZoneOverrideId;
        _useSystemTheme = _model.Theme == AppTheme.System;
        UpdatePinnedThemeState();
        _detectedTimeZoneLabel = TimeZoneService.TimeZoneId ?? "Unknown";
        UpdateTimeZoneList();
        UpdateDatePreview();
        UpdateResolvedTimeZoneLabel();
    }

    private void UpdateTimeZoneList()
    {
        _timeZones.Clear();
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            _timeZones.Add((tz.Id, $"{tz.DisplayName} ({tz.Id})"));
        }
    }

    private void UpdateDatePreview()
    {
        if (_model is null)
        {
            return;
        }
        _dateFormatError = null;
        try
        {
            _datePreview = DateTime.Now.ToString(_model.DateFormat, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            _datePreview = "Invalid format";
            _dateFormatError = "Date format is not valid.";
        }
    }

    private void UpdateResolvedTimeZoneLabel()
    {
        _resolvedTimeZoneLabel = _timeZoneSelection == "Auto"
            ? _detectedTimeZoneLabel
            : _timeZoneSelection;
    }

    private void ApplyTimeZoneOverride()
    {
        TimeZoneService.SetOverride(_model?.TimeZoneOverrideId);
    }

    private static UserPreferences Clone(UserPreferences s)
    {
        return UserPreferencesJson.DeserializeOrDefault(UserPreferencesJson.Serialize(s));
    }

    private async Task AutoSaveAsync()
    {
        if (_model is null)
        {
            return;
        }
        if (!ValidateInputs())
        {
            return;
        }
        try
        {
            await PreferencesService.SaveAsync(_model);
            ApplyTimeZoneOverride();
        }
        catch (Exception ex)
        {
            await Notifier.NotifyAsync($"Could not save preferences: {ex.Message}", Severity.Error);
        }
    }

    private void OnDisplayNameChanged(string value)
    {
        if (_model is null)
        {
            return;
        }
        // Strip Zalgo characters silently before storing - the sanitised value is
        // what the user will see after blur, which makes the effect self-evident.
        var sanitized = ZalgoSanitizer.Sanitize(value);
        _displayName = sanitized ?? string.Empty;
        _model.DisplayName = string.IsNullOrWhiteSpace(sanitized) ? null : sanitized.Trim();
        _displayNameError = null;
    }

    private void OnDateFormatChanged(string value)
    {
        if (_model is null)
        {
            return;
        }
        _model.DateFormat = value;
        UpdateDatePreview();
    }


    private async Task OnDayStartChanged(TimeSpan? value)
    {
        if (_model is null || value is null)
        {
            return;
        }
        _dayStart = value;
        _model.DayStartLocalTime = value.Value;
        await AutoSaveAsync();
    }

    private async Task OnTimeZoneChanged(string value)
    {
        if (_model is null)
        {
            return;
        }
        _timeZoneSelection = value;
        _model.TimeZoneOverrideId = value == AutoTimeZoneValue ? null : value;
        UpdateResolvedTimeZoneLabel();
        await AutoSaveAsync();
    }

    private async Task OnUseSystemThemeChanged(bool useSystem)
    {
        if (_model is null)
        {
            return;
        }
        _useSystemTheme = useSystem;
        if (useSystem)
        {
            _model.Theme = AppTheme.System;
        }
        else
        {
            // Pin to dark mode, the app default, when unpinning from system. User can toggle with button
            _model.Theme = AppTheme.Dark;
        }
        UpdatePinnedThemeState();
        await AutoSaveAsync();
    }

    private async Task TogglePinnedTheme()
    {
        if (_model is null)
        {
            return;
        }
        // Flip pinned theme: light to dark, dark to light
        _model.Theme = _model.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        UpdatePinnedThemeState();
        await AutoSaveAsync();
    }

    private void UpdatePinnedThemeState()
    {
        if (_model is null)
        {
            return;
        }
        if (_model.Theme == AppTheme.System)
        {
            _useSystemTheme = true;
            _pinnedThemeBtnLabel = "Switch theme";
            _pinnedThemeBtnVariant = Variant.Outlined;
            _themeStatusText = "Following system preference";
        }
        else
        {
            _useSystemTheme = false;
            var pinned = _model.Theme == AppTheme.Dark ? "dark" : "light";
            var opposite = _model.Theme == AppTheme.Dark ? "light" : "dark";
            _pinnedThemeBtnLabel = $"{char.ToUpper(opposite[0], CultureInfo.InvariantCulture) + opposite[1..]} mode";
            _pinnedThemeBtnVariant = Variant.Filled;
            _themeStatusText = $"{char.ToUpper(pinned[0], CultureInfo.InvariantCulture) + pinned[1..]} theme, pinned";
        }
    }

    private async Task OnEnableKeyboardShortcutsChanged(bool value)
    {
        if (_model is null)
        {
            return;
        }
        _model.EnableKeyboardShortcuts = value;
        await AutoSaveAsync();
    }

    private void OnWorkDurationChanged(int value)
    {
        var model = _model;
        if (model is null)
        {
            return;
        }
        ClampPomodoroValue(v => model.PomodoroWorkDurationMinutes = v, 1, 180, value);
    }

    private void OnShortBreakChanged(int value)
    {
        var model = _model;
        if (model is null)
        {
            return;
        }
        ClampPomodoroValue(v => model.PomodoroShortBreakMinutes = v, 1, 60, value);
    }

    private void OnLongBreakChanged(int value)
    {
        var model = _model;
        if (model is null)
        {
            return;
        }
        ClampPomodoroValue(v => model.PomodoroLongBreakMinutes = v, 1, 120, value);
    }

    private void OnCyclesChanged(int value)
    {
        var model = _model;
        if (model is null)
        {
            return;
        }
        ClampPomodoroValue(v => model.PomodoroCyclesBeforeLongBreak = v, 1, 12, value);
    }

    private void ClampPomodoroValue(Action<int> set, int min, int max, int value)
    {
        if (_model is null)
        {
            return;
        }
        set(Math.Clamp(value, min, max));
    }

    private bool ValidateInputs()
    {
        _displayNameError = null;
        _dateFormatError = null;
        _dayStartError = null;

        if (_model is null)
        {
            return false;
        }

        _model.Normalize();

        if (!string.IsNullOrWhiteSpace(_model.DisplayName) && _model.DisplayName.Length > 40)
        {
            _displayNameError = "Display name must be 40 characters or fewer.";
        }

        try
        {
            _ = DateTime.Now.ToString(_model.DateFormat, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            _dateFormatError = "Date format is not valid.";
        }

        return _displayNameError is null && _dateFormatError is null && _dayStartError is null;
    }

}
