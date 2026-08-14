using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Data;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class WebNotificationSettingsService(
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IBoardChangeNotifier boardChangeNotifier) : INotificationSettingsService
{
    public event EventHandler? Changed;

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = AuthenticatedUserId.TryGet(state.User);
        if (userId is null)
        {
            return NotificationSettings.CreateDefault();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return row?.NotificationSettings ?? NotificationSettings.CreateDefault();
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
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

        row.NotificationSettings = settings;
        await db.SaveChangesAsync(cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
        await boardChangeNotifier.NotifyBoardChangedAsync(userId.Value, cancellationToken);
    }
}
