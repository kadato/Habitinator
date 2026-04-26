using System.Security.Claims;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class WebNotificationSettingsService : INotificationSettingsService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly DemoUserResolver _demoUserResolver;
    private readonly ApplicationDbContext _db;

    public WebNotificationSettingsService(
        AuthenticationStateProvider authenticationStateProvider,
        DemoUserResolver demoUserResolver,
        ApplicationDbContext db)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _demoUserResolver = demoUserResolver;
        _db = db;
    }

    public event Action? Changed;

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        AuthenticationState state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        ClaimsPrincipal user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return NotificationSettings.CreateDefault();
        }

        Guid userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        ApplicationUser? row = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null)
        {
            return NotificationSettings.CreateDefault();
        }

        return NotificationSettingsJson.DeserializeOrDefault(row.NotificationSettingsJson);
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        AuthenticationState state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        ClaimsPrincipal user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return;
        }

        Guid userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        ApplicationUser? row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.NotificationSettingsJson = NotificationSettingsJson.Serialize(settings);
        await _db.SaveChangesAsync(cancellationToken);
        Changed?.Invoke();
    }
}
