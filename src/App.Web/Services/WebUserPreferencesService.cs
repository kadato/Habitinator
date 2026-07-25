using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class WebUserPreferencesService(
    AuthenticationStateProvider authenticationStateProvider,
    DemoUserResolver demoUserResolver,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IBoardChangeNotifier boardChangeNotifier,
    IHttpContextAccessor httpContextAccessor,
    ILogger<WebUserPreferencesService> logger) : IUserPreferencesService
{
    private Guid? _cachedUserId;
    private UserPreferences? _cachedPreferences;

    public event EventHandler? Changed;

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            var user = state.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                _cachedUserId = null;
                _cachedPreferences = null;
                var guestPrefs = UserPreferences.CreateDefault();
                ApplyThemeFromCookie(guestPrefs);
                return guestPrefs;
            }

            var userId = await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
            if (_cachedUserId == userId && _cachedPreferences is not null)
            {
                return _cachedPreferences;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            var prefs = row?.UserPreferences ?? UserPreferences.CreateDefault();

            if (prefs.Theme == AppTheme.System)
            {
                ApplyThemeFromCookie(prefs);
            }

            _cachedUserId = userId;
            _cachedPreferences = prefs;
            return prefs;
        }
        catch (Exception ex)
        {
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
            if (httpContext is not null && httpContext.Request.Cookies.TryGetValue("habitinator_theme", out var themeCookie))
            {
                if (themeCookie == "light")
                {
                    prefs.Theme = AppTheme.Light;
                }
                else if (themeCookie == "dark")
                {
                    prefs.Theme = AppTheme.Dark;
                }
            }
        }
        catch
        {
            // Ignore context or cookie reading issues during prerendering/tasks
        }
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userId = await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.UserPreferences = preferences;
        await db.SaveChangesAsync(cancellationToken);
        _cachedUserId = userId;
        _cachedPreferences = preferences;
        Changed?.Invoke(this, EventArgs.Empty);
        await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
    }
}
