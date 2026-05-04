using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class WebUserPreferencesService : IUserPreferencesService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IBoardChangeNotifier _boardChangeNotifier;
    private readonly ApplicationDbContext _db;
    private readonly DemoUserResolver _demoUserResolver;

    public WebUserPreferencesService(
        AuthenticationStateProvider authenticationStateProvider,
        DemoUserResolver demoUserResolver,
        ApplicationDbContext db,
        IBoardChangeNotifier boardChangeNotifier)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _demoUserResolver = demoUserResolver;
        _db = db;
        _boardChangeNotifier = boardChangeNotifier;
    }

    public event Action? Changed;

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true) return UserPreferences.CreateDefault();

        var userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        var row = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null) return UserPreferences.CreateDefault();

        return UserPreferencesJson.DeserializeOrDefault(row.UserPreferencesJson);
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true) return;

        var userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null) return;

        row.UserPreferencesJson = UserPreferencesJson.Serialize(preferences);
        await _db.SaveChangesAsync(cancellationToken);
        Changed?.Invoke();
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
    }
}
