using System.Security.Cryptography;

using App.Shared.RCL.Models;
using App.Web.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Web.Services;

/// <summary>
/// Creates a throwaway copy of the seeded demo board for one visitor. The copy owns its user
/// row and board rows, so edits never reach another visitor's board. Copies expire and are
/// removed by <see cref="CleanupExpiredAsync" />.
/// </summary>
public sealed class DemoGuestCloneService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IServiceProvider services,
    IOptions<DemoUserOptions> demoOptions,
    IOptions<DemoGuestCloneOptions> cloneOptions,
    ILogger<DemoGuestCloneService> logger)
{
    private const int EmailAttempts = 5;
    private const int CleanupBatchSize = 500;

    /// <summary>Creates a fully seeded copy of the demo board. Returns null when the template is unavailable.</summary>
    public async Task<ApplicationUser?> CreateAsync(CancellationToken cancellationToken = default)
    {
        // A cold start can race the background seed. Wait for the template so a clone never
        // starts from an empty board. If the schema is not ready yet, the full seed runs the
        // migrations before it creates the template.
        try
        {
            await DemoDataSeeder.EnsureGuestReadyAsync(services, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Demo template preparation failed. Running the full demo seed.");
            await DemoDataSeeder.SeedAsync(services, cancellationToken);
        }

        var templateEmail = demoOptions.Value.Email;
        Guid templateUserId;
        await using (var readDb = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var template = await readDb.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == templateEmail, cancellationToken);
            if (template is null)
            {
                logger.LogWarning("Demo template user {Email} is missing. A guest copy cannot be created.", templateEmail);
                return null;
            }

            templateUserId = template.Id;
        }

        await MakeRoomAsync(cancellationToken);

        var clone = await CreateUserAsync(templateEmail);
        if (clone is null)
        {
            return null;
        }

        try
        {
            await CopyBoardAsync(templateUserId, clone.Id, cancellationToken);
        }
        catch
        {
            await DeleteUserQuietlyAsync(clone);
            throw;
        }

        logger.LogInformation("Created demo guest copy {UserId}.", clone.Id);
        return clone;
    }

    /// <summary>True when the account is a temporary demo copy.</summary>
    public async Task<bool> IsCloneAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DemoGuestClones.AsNoTracking().AnyAsync(c => c.UserId == userId, cancellationToken);
    }

    /// <summary>True for the demo template or any copy of it. Guest AI quotas use this.</summary>
    public async Task<bool> IsGuestAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var email = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.Equals(email, demoOptions.Value.Email, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return await db.DemoGuestClones.AsNoTracking()
            .AnyAsync(c => c.UserId == userId, cancellationToken);
    }

    /// <summary>Deletes one copy and its rows. The demo template is never deleted.</summary>
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.DemoGuestClones.AsNoTracking().AnyAsync(c => c.UserId == userId, cancellationToken))
        {
            return;
        }

        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Deletes copies past their lifetime. Returns the number of removed accounts.</summary>
    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-cloneOptions.Value.LifetimeMinutes);
        var ids = await db.DemoGuestClones.AsNoTracking()
            .Where(c => c.CreatedAtUtc < cutoff)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => c.UserId)
            .Take(CleanupBatchSize)
            .ToListAsync(cancellationToken);

        return await DeleteUsersAsync(db, ids, cancellationToken);
    }

    private async Task MakeRoomAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-cloneOptions.Value.LifetimeMinutes);
        var expired = await db.DemoGuestClones.AsNoTracking()
            .Where(c => c.CreatedAtUtc < cutoff)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => c.UserId)
            .Take(CleanupBatchSize)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            await DeleteUsersAsync(db, expired, cancellationToken);
        }

        var max = cloneOptions.Value.MaxActiveClones;
        var active = await db.DemoGuestClones.AsNoTracking().CountAsync(cancellationToken);
        if (active < max)
        {
            return;
        }

        var oldest = await db.DemoGuestClones.AsNoTracking()
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => c.UserId)
            .Take(active - max + 1)
            .ToListAsync(cancellationToken);
        await DeleteUsersAsync(db, oldest, cancellationToken);
    }

    private async Task<int> DeleteUsersAsync(
        ApplicationDbContext db,
        List<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return 0;
        }

        // The foreign keys cascade to the board rows, activity log, tokens, and quota rows.
        var deleted = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation("Removed {Count} demo guest copies.", deleted);
        }

        return deleted;
    }

    private async Task<ApplicationUser?> CreateUserAsync(string templateEmail)
    {
        for (var attempt = 0; attempt < EmailAttempts; attempt++)
        {
            var email = BuildCloneEmail(templateEmail);
            var clone = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                UserPreferences = UserPreferences.CreateDefault(),
                NotificationSettings = NotificationSettings.CreateDefault()
            };

            var result = await userManager.CreateAsync(clone);
            if (result.Succeeded)
            {
                return clone;
            }

            if (result.Errors.All(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
            {
                continue;
            }

            logger.LogWarning(
                "Creating a demo guest copy failed: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return null;
        }

        logger.LogWarning("Creating a demo guest copy failed after {Attempts} email attempts.", EmailAttempts);
        return null;
    }

    private async Task CopyBoardAsync(Guid templateUserId, Guid cloneUserId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var templateItems = await db.BoardItems.AsNoTracking()
            .Where(x => x.UserId == templateUserId && x.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);

        var idMap = new Dictionary<Guid, Guid>(templateItems.Count);
        var cloneItems = new List<BoardItemEntity>(templateItems.Count);
        foreach (var item in templateItems)
        {
            var cloneItemId = Guid.NewGuid();
            idMap[item.Id] = cloneItemId;
            cloneItems.Add(CloneItem(item, cloneUserId, cloneItemId));
        }

        var templateEvents = await db.UserActivityEvents.AsNoTracking()
            .Where(x => x.UserId == templateUserId)
            .ToListAsync(cancellationToken);
        var cloneEvents = new List<UserActivityEventEntity>(templateEvents.Count);
        foreach (var activityEvent in templateEvents)
        {
            cloneEvents.Add(CloneActivityEvent(activityEvent, cloneUserId, idMap));
        }

        db.BoardItems.AddRange(cloneItems);
        db.UserActivityEvents.AddRange(cloneEvents);
        db.DemoGuestClones.Add(new DemoGuestCloneEntity
        {
            UserId = cloneUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteUserQuietlyAsync(ApplicationUser clone)
    {
        try
        {
            using var scope = services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByIdAsync(clone.Id.ToString());
            if (user is not null)
            {
                await manager.DeleteAsync(user);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rolling back an incomplete demo guest copy {UserId} failed. Cleanup will retry.", clone.Id);
        }
    }

    private static BoardItemEntity CloneItem(BoardItemEntity item, Guid cloneUserId, Guid cloneItemId) =>
        new()
        {
            Id = cloneItemId,
            UserId = cloneUserId,
            Section = item.Section,
            Title = item.Title,
            Notes = item.Notes,
            Tags = item.Tags,
            TrackPlus = item.TrackPlus,
            TrackMinus = item.TrackMinus,
            ResetPeriod = item.ResetPeriod,
            HabitPeriodStart = item.HabitPeriodStart,
            IsCompleted = item.IsCompleted,
            Counter = item.Counter,
            NegativeCounter = item.NegativeCounter,
            TargetCount = item.TargetCount,
            DailyStartDate = item.DailyStartDate,
            DailyRepeatType = item.DailyRepeatType,
            DailyRepeatInterval = item.DailyRepeatInterval,
            DailyWeekdays = item.DailyWeekdays,
            ChecklistJson = item.ChecklistJson,
            DailyLastCompletedOn = item.DailyLastCompletedOn,
            DailyPostponedUntil = item.DailyPostponedUntil,
            DailyPostponedFrom = item.DailyPostponedFrom,
            DailySkippedOn = item.DailySkippedOn,
            CreatedAtUtc = item.CreatedAtUtc,
            UpdatedAtUtc = item.UpdatedAtUtc,
            SortOrder = item.SortOrder,
            DeletedAtUtc = null,
            IsArchived = item.IsArchived
        };

    private static UserActivityEventEntity CloneActivityEvent(
        UserActivityEventEntity activityEvent,
        Guid cloneUserId,
        Dictionary<Guid, Guid> boardItemIdMap) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = cloneUserId,
            OccurredAtUtc = activityEvent.OccurredAtUtc,
            EventType = activityEvent.EventType,
            BoardItemId = activityEvent.BoardItemId is { } boardItemId
                           && boardItemIdMap.TryGetValue(boardItemId, out var mappedId)
                ? mappedId
                : null,
            DurationSeconds = activityEvent.DurationSeconds,
            CustomLabel = activityEvent.CustomLabel,
            // The copy is a new account, so client-generated ids carry no meaning here.
            EventId = null
        };

    private static string BuildCloneEmail(string templateEmail)
    {
        var at = templateEmail.IndexOf('@', StringComparison.Ordinal);
        var local = at > 0 ? templateEmail[..at] : "guest";
        var domain = at > 0 ? templateEmail[(at + 1)..] : "habitinator.local";
        return $"{local}+{RandomNumberGenerator.GetHexString(7, lowercase: true)}@{domain}";
    }
}
