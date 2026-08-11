using System.Globalization;

using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public sealed class UserDateFormatService : IUserDateFormatService, IDisposable
{
    private readonly IUserPreferencesService _preferencesService;
    private readonly ILogger<UserDateFormatService> _logger;
    private string _dateFormat = UserPreferences.DefaultDateFormat;
    private bool _initialized;

    public UserDateFormatService(IUserPreferencesService preferencesService, ILogger<UserDateFormatService> logger)
    {
        _preferencesService = preferencesService;
        _logger = logger;
        _preferencesService.Changed += OnPreferencesChanged;
    }

    public string DateFormat => _dateFormat;

    public void ApplyFromPreferences(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _dateFormat = NormalizeFormat(preferences.DateFormat);
        _initialized = true;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        var prefs = await _preferencesService.GetAsync(cancellationToken).ConfigureAwait(false);
        ApplyFromPreferences(prefs);
    }

    public string Format(DateOnly dateValue)
    {
        return dateValue.ToString(EffectiveFormat, CultureInfo.InvariantCulture);
    }

    public string Format(DateTime dateTime)
    {
        return dateTime.ToString($"{EffectiveFormat} HH:mm", CultureInfo.InvariantCulture);
    }

    private string EffectiveFormat => _initialized ? _dateFormat : UserPreferences.DefaultDateFormat;

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        _ = RefreshFormatAsync();
    }

    private async Task RefreshFormatAsync()
    {
        try
        {
            var prefs = await _preferencesService.GetAsync().ConfigureAwait(false);
            _dateFormat = NormalizeFormat(prefs.DateFormat);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh date format from preferences; using default.");
            _dateFormat = UserPreferences.DefaultDateFormat;
        }
    }

    private static string NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return UserPreferences.DefaultDateFormat;
        }

        try
        {
            // Validate format string
            _ = DateTime.UnixEpoch.ToString(format, CultureInfo.InvariantCulture);
            return format;
        }
        catch (FormatException)
        {
            return UserPreferences.DefaultDateFormat;
        }
    }

    public void Dispose()
    {
        _preferencesService.Changed -= OnPreferencesChanged;
    }
}
