using App.Web.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Web.Services;

public sealed class BoardMaintenanceOptions
{
    public const string SectionName = "BoardMaintenance";

    /// <summary>Idempotency rows older than this are purged.</summary>
    public TimeSpan IdempotencyRetention { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Soft-deleted board rows older than this are physically removed.</summary>
    public TimeSpan TombstoneRetention { get; set; } = TimeSpan.FromDays(90);

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
}

/// <summary>Periodic purge of idempotency records and compacted tombstones.</summary>
public sealed class BoardMaintenanceHostedService(
    IServiceProvider services,
    IOptions<BoardMaintenanceOptions> options,
    ILogger<BoardMaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        using var timer = new PeriodicTimer(opts.Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(opts, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Board maintenance sweep failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // app stop
        }
    }

    private async Task RunOnceAsync(BoardMaintenanceOptions opts, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var idemCutoff = DateTimeOffset.UtcNow - opts.IdempotencyRetention;
        var tombCutoff = DateTimeOffset.UtcNow - opts.TombstoneRetention;

        var idemRemoved = await db.BoardRequestIdempotencies
            .Where(x => x.CreatedAtUtc < idemCutoff)
            .ExecuteDeleteAsync(ct);

        var tombRemoved = await db.BoardItems
            .Where(x => x.DeletedAtUtc != null && x.DeletedAtUtc < tombCutoff)
            .ExecuteDeleteAsync(ct);

        // Recurring to-dos: when their advanced due date has begun, bring them back to the active
        // board. Due dates are stored as UTC midnight of the user's local date, so `due <= now`
        // means the local date has started in every time zone.
        var recurringRolledBack = await db.BoardItems
            .Where(x => x.TodoRepeatIntervalDays != null
                        && x.TodoRepeatIntervalDays > 0
                        && x.Section == App.Shared.RCL.Models.BoardSection.Todo
                        && x.IsCompleted
                        && x.DailyStartDate != null
                        && x.DailyStartDate <= DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.IsCompleted, false),
                ct);

        if (idemRemoved > 0 || tombRemoved > 0 || recurringRolledBack > 0)
        {
            logger.LogInformation(
                "Board maintenance: removed {Idem} idempotency rows, {Tomb} tombstoned items, rolled {Recurring} recurring to-dos back to active.",
                idemRemoved,
                tombRemoved,
                recurringRolledBack);
        }
    }
}
