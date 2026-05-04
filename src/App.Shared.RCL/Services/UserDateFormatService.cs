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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        var prefs = await _preferencesService.GetAsync(cancellationToken).ConfigureAwait(false);
        _dateFormat = NormalizeFormat(prefs.DateFormat);
        _initialized = true;
    }

    public string Format(DateOnly date)
    {
        var format = _initialized ? _dateFormat : UserPreferences.CreateDefault().DateFormat;
        return date.ToString(format, CultureInfo.CurrentCulture);
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
            _dateFormat = UserPreferences.CreateDefault().DateFormat;
        }
    }

    private static string NormalizeFormat(string? format)
    {
        return string.IsNullOrWhiteSpace(format)
            ? UserPreferences.CreateDefault().DateFormat
            : format;
    }
}
