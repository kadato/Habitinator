using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class WebNotificationSettingsService(
    AuthenticationStateProvider authenticationStateProvider,
    DemoUserResolver demoUserResolver,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IBoardChangeNotifier boardChangeNotifier) : INotificationSettingsService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider = authenticationStateProvider;
    private readonly IBoardChangeNotifier _boardChangeNotifier = boardChangeNotifier;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;
    private readonly DemoUserResolver _demoUserResolver = demoUserResolver;

    public event Action? Changed;

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return NotificationSettings.CreateDefault();
        }

        var userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return row?.NotificationSettings ?? NotificationSettings.CreateDefault();
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.NotificationSettings = settings;
        await db.SaveChangesAsync(cancellationToken);
        Changed?.Invoke();
        await _boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
    }
}
