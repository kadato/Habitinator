using System.Globalization;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class UserDateFormatService : IUserDateFormatService
{
    private readonly IUserPreferencesService _preferencesService;
    private string _dateFormat = UserPreferences.CreateDefault().DateFormat;
    private bool _initialized;

    public UserDateFormatService(IUserPreferencesService preferencesService)
    {
        _preferencesService = preferencesService;
        _preferencesService.Changed += OnPreferencesChanged;
    }

    public string DateFormat => _dateFormat;

    public void ApplyFromPreferences(UserPreferences preferences)
    {
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
        var format = _initialized ? _dateFormat : UserPreferences.CreateDefault().DateFormat;
        return dateValue.ToString(format, CultureInfo.InvariantCulture);
    }

    public string Format(DateTime dateTime)
    {
        var format = _initialized ? _dateFormat : UserPreferences.CreateDefault().DateFormat;
        return dateTime.ToString($"{format} HH:mm", CultureInfo.InvariantCulture);
    }

    public string Format(DateTimeOffset dateTimeOffset)
    {
        var format = _initialized ? _dateFormat : UserPreferences.CreateDefault().DateFormat;
        return dateTimeOffset.ToString($"{format} HH:mm", CultureInfo.InvariantCulture);
    }

    private async void OnPreferencesChanged()
    {
        try
        {
            var prefs = await _preferencesService.GetAsync().ConfigureAwait(false);
            _dateFormat = NormalizeFormat(prefs.DateFormat);
        }
        catch
        {
            // Fall back to default format on preference retrieval error
            _dateFormat = UserPreferences.CreateDefault().DateFormat;
        }
    }

    private static string NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return UserPreferences.CreateDefault().DateFormat;
        }

        try
        {
            // Validate format string
            _ = DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
            return format;
        }
        catch
        {
            return UserPreferences.CreateDefault().DateFormat;
        }
    }
}
