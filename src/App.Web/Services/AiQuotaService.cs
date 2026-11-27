using System.Text.Json.Nodes;

using App.Web.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Web.Services;

/// <summary>
///     Enforces a daily request and token budget for the shared operator AI key.
///     User-owned keys are not counted because the user pays for those calls.
/// </summary>
public sealed class AiQuotaService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IOptions<AiQuotaOptions> options,
    ILogger<AiQuotaService> logger)
{
    public const string LimitMessage =
        "Daily AI limit reached for the shared key. Add your own API key in Settings to keep going.";

    public async Task<string?> TryBeginRequestAsync(Guid userId, bool isGuest, CancellationToken cancellationToken = default)
    {
        var limits = GetLimits(isGuest);
        var day = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.AiUsageDaily
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Day == day, cancellationToken);

            if (row is null)
            {
                db.AiUsageDaily.Add(new AiUsageDailyEntity
                {
                    UserId = userId,
                    Day = day,
                    Requests = 1
                });

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    return null;
                }
                catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
                {
                    // Another request for this user inserted the row first.
                    continue;
                }
            }

            if (row.Requests >= limits.Requests
                || row.InputTokens + row.OutputTokens >= limits.Tokens)
            {
                return LimitMessage;
            }

            row.Requests++;
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        logger.LogWarning("Could not reserve AI quota for user {UserId} after retries.", userId);
        return "The AI quota could not be updated. Try again in a moment.";
    }

    public async Task RecordUsageAsync(
        Guid userId,
        long inputTokens,
        long outputTokens,
        CancellationToken cancellationToken = default)
    {
        if (inputTokens <= 0 && outputTokens <= 0)
        {
            return;
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            var row = await db.AiUsageDaily
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Day == day, cancellationToken);
            if (row is null)
            {
                db.AiUsageDaily.Add(new AiUsageDailyEntity
                {
                    UserId = userId,
                    Day = day,
                    Requests = 0,
                    InputTokens = Math.Max(0, inputTokens),
                    OutputTokens = Math.Max(0, outputTokens)
                });
            }
            else
            {
                row.InputTokens += Math.Max(0, inputTokens);
                row.OutputTokens += Math.Max(0, outputTokens);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            // A concurrent reservation created the row. The next provider call
            // records the tokens. Never fail a chat because accounting raced.
            logger.LogDebug(ex, "AI token accounting raced with another request for user {UserId}.", userId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to record AI token usage for user {UserId}.", userId);
        }
    }

    public (int Requests, long Tokens) GetLimits(bool isGuest)
    {
        var value = options.Value;
        return isGuest
            ? (Math.Max(1, value.GuestSharedKeyDailyRequests), Math.Max(1, value.GuestSharedKeyDailyTokens))
            : (Math.Max(1, value.SharedKeyDailyRequests), Math.Max(1, value.SharedKeyDailyTokens));
    }

    /// <summary>Reads usage from either Responses API or Chat Completions payloads.</summary>
    public static (long InputTokens, long OutputTokens) ReadUsage(JsonNode? root)
    {
        if (root is not JsonObject rootObject
            || rootObject["usage"] is not JsonObject usage)
        {
            return (0, 0);
        }

        var input = ReadLong(usage["input_tokens"]) ?? ReadLong(usage["prompt_tokens"]) ?? 0;
        var output = ReadLong(usage["output_tokens"]) ?? ReadLong(usage["completion_tokens"]) ?? 0;
        return (input, output);
    }

    private static long? ReadLong(JsonNode? node)
    {
        try
        {
            return node?.GetValue<long>();
        }
        catch
        {
            return null;
        }
    }
}
