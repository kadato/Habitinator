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
    public event EventHandler? Changed;

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return NotificationSettings.CreateDefault();
        }

        var userId = await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return row?.NotificationSettings ?? NotificationSettings.CreateDefault();
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
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

        row.NotificationSettings = settings;
        await db.SaveChangesAsync(cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
        await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
    }
}
