using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>
///     Encrypts provider API keys that were written before encryption existed.
///     Runs once in the background so a deployment does not keep plaintext keys
///     in the preferences JSONB column until each user opens Settings.
/// </summary>
public sealed class AiApiKeyEncryptionHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiApiKeyEncryptionHostedService> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private const int MaxAttempts = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await Task.Yield();
                var changed = await EncryptExistingKeysAsync(scopeFactory, stoppingToken);
                if (changed > 0)
                {
                    logger.LogInformation("Encrypted {Count} existing AI API keys at rest.", changed);
                }

                return;
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogDebug(ex, "AI API key encryption will retry after the database is ready.");
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not encrypt existing AI API keys. They will be encrypted on the next save.");
                return;
            }
        }
    }

    internal static async Task<int> EncryptExistingKeysAsync(
        IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken)
    {
        var changed = 0;
        var offset = 0;

        while (true)
        {
            using var scope = scopeFactory.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            var protector = scope.ServiceProvider.GetRequiredService<AiApiKeyProtector>();
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var batch = await db.Users
                .Where(u => u.UserPreferences != null)
                .OrderBy(u => u.Id)
                .Skip(offset)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            var batchChanged = 0;
            foreach (var user in batch)
            {
                if (user.UserPreferences is not { Ai: { } ai })
                {
                    continue;
                }

                var key = ai.ApiKey;
                if (string.IsNullOrWhiteSpace(key) || AiApiKeyProtector.IsProtectedValue(key))
                {
                    continue;
                }

                user.UserPreferences = AiPreferencesPrivacy.ApplyWrite(
                    user.UserPreferences,
                    existing: null,
                    protector);
                batchChanged++;
            }

            if (batchChanged > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                changed += batchChanged;
            }

            offset += batch.Count;
        }

        return changed;
    }
}
