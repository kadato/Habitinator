using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Data;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class WebUserPreferencesService(
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IBoardChangeNotifier boardChangeNotifier,
    IHttpContextAccessor httpContextAccessor,
    IUserTimeZoneService timeZone,
    AiApiKeyProtector apiKeyProtector,
    ILogger<WebUserPreferencesService> logger) : IUserPreferencesService
{
    private Guid? _cachedUserId;
    private UserPreferences? _cachedPreferences;
    private bool _lastReadFailed;

    public event EventHandler? Changed;

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            var userId = AuthenticatedUserId.TryGet(state.User);
            if (userId is null)
            {
                _cachedUserId = null;
                _cachedPreferences = null;
                _lastReadFailed = false;
                var guestPrefs = UserPreferences.CreateDefault();
                ApplyThemeFromCookie(guestPrefs);
                return guestPrefs;
            }

            if (_cachedUserId == userId && _cachedPreferences is not null)
            {
                return AiPreferencesPrivacy.MaskForClient(_cachedPreferences);
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            var prefs = row?.UserPreferences ?? UserPreferences.CreateDefault();
            timeZone.SetOverride(prefs.TimeZoneOverrideId);

            if (prefs.Theme == AppTheme.System)
            {
                ApplyThemeFromCookie(prefs);
            }

            _cachedUserId = userId;
            _cachedPreferences = prefs;
            _lastReadFailed = false;
            return AiPreferencesPrivacy.MaskForClient(prefs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The fallback keeps the UI usable, but a later save must not write
            // these defaults over the user's real preferences.
            _lastReadFailed = true;
            logger.LogWarning(ex, "Could not load user preferences; using defaults.");
            var fallbackPrefs = UserPreferences.CreateDefault();
            ApplyThemeFromCookie(fallbackPrefs);
            return fallbackPrefs;
        }
    }

    private void ApplyThemeFromCookie(UserPreferences prefs)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext is not null && httpContext.Request.Cookies.TryGetValue(ThemeCookie.Name, out var themeCookie))
            {
                prefs.Theme = themeCookie switch
                {
                    "light" => AppTheme.Light,
                    "dark" => AppTheme.Dark,
                    _ => prefs.Theme
                };
            }
        }
        catch
        {
            // Ignore context or cookie reading issues during prerendering/tasks
        }
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        if (_lastReadFailed)
        {
            throw new InvalidOperationException("Preferences couldn't be loaded. Reopen Settings and try again.");
        }

        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = AuthenticatedUserId.TryGet(state.User);
        if (userId is null)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null)
        {
            return;
        }

        var saved = AiPreferencesPrivacy.ApplyWrite(preferences, row.UserPreferences, apiKeyProtector);
        row.UserPreferences = saved;
        await db.SaveChangesAsync(cancellationToken);
        _cachedUserId = userId;
        _cachedPreferences = saved;
        Changed?.Invoke(this, EventArgs.Empty);
        await boardChangeNotifier.NotifyBoardChangedAsync(userId.Value, cancellationToken);
    }
}
