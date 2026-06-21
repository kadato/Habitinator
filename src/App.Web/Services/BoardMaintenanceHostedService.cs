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

        if (idemRemoved > 0 || tombRemoved > 0)
        {
            logger.LogInformation(
                "Board maintenance: removed {Idem} idempotency rows, {Tomb} tombstoned items.",
                idemRemoved,
                tombRemoved);
        }
    }
}
