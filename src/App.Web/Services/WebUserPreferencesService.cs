using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Web.Services;

public sealed class WebUserPreferencesService : IUserPreferencesService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IBoardChangeNotifier _boardChangeNotifier;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly DemoUserResolver _demoUserResolver;
    private readonly ILogger<WebUserPreferencesService> _logger;
    private Guid? _cachedUserId;
    private UserPreferences? _cachedPreferences;

    public WebUserPreferencesService(
        AuthenticationStateProvider authenticationStateProvider,
        DemoUserResolver demoUserResolver,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IBoardChangeNotifier boardChangeNotifier,
        ILogger<WebUserPreferencesService> logger)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _demoUserResolver = demoUserResolver;
        _dbFactory = dbFactory;
        _boardChangeNotifier = boardChangeNotifier;
        _logger = logger;
    }

    public event Action? Changed;

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var user = state.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                _cachedUserId = null;
                _cachedPreferences = null;
                return UserPreferences.CreateDefault();
            }

            var userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
            if (_cachedUserId == userId && _cachedPreferences is not null)
                return _cachedPreferences;

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            var prefs = row is null
                ? UserPreferences.CreateDefault()
                : UserPreferencesJson.DeserializeOrDefault(row.UserPreferencesJson);
            _cachedUserId = userId;
            _cachedPreferences = prefs;
            return prefs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load user preferences; using defaults.");
            return UserPreferences.CreateDefault();
        }
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true) return;

        var userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null) return;

        row.UserPreferencesJson = UserPreferencesJson.Serialize(preferences);
        await db.SaveChangesAsync(cancellationToken);
        _cachedUserId = userId;
        _cachedPreferences = preferences;
        Changed?.Invoke();
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
    }
}
