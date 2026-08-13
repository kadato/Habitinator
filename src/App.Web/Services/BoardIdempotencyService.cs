using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>Idempotent replay for board mutations, using the Idempotency-Key and request fingerprint.</summary>
public sealed class BoardIdempotencyService(
    ApplicationDbContext db,
    ILogger<BoardIdempotencyService> logger)
{
    public const int PendingResponseCode = -1;

    private static readonly JsonSerializerOptions s_problemJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ComputeFingerprintHex(string httpMethod, string path, string bodyJson)
    {
        var text = $"{httpMethod.ToUpperInvariant()}\n{path}\n{bodyJson ?? ""}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>When no idempotency key, runs <paramref name="execute"/> directly. Otherwise stores/replays the response.</summary>
    public async Task<(int statusCode, string body, string? contentType)> RunAsync(
        Guid userId,
        string? idempotencyKey,
        string fingerprintHex,
        Func<Task<(int statusCode, string body, string? contentType)>> execute,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return await execute();
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = await db.BoardRequestIdempotencies.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (row is not null)
            {
                if (!string.Equals(row.RequestFingerprintHex, fingerprintHex, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BoardIdempotencyFingerprintMismatchException();
                }

                if (row.ResponseStatusCode != PendingResponseCode)
                {
                    return (row.ResponseStatusCode, row.ResponseBody, "application/json");
                }

                await WaitForOtherAsync(userId, idempotencyKey, fingerprintHex, cancellationToken);
                continue;
            }

            var claim = new BoardRequestIdempotencyEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                IdempotencyKey = idempotencyKey,
                RequestFingerprintHex = fingerprintHex,
                ResponseStatusCode = PendingResponseCode,
                ResponseBody = "",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.BoardRequestIdempotencies.Add(claim);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.Entry(claim).State = EntityState.Detached; // Detach failed entry from change tracker
                await Task.Delay(25, cancellationToken);
                continue;
            }

            return await ExecuteAndSaveOutcomeAsync(claim, execute, idempotencyKey, cancellationToken);
        }
    }

    private async Task<(int statusCode, string body, string? contentType)> ExecuteAndSaveOutcomeAsync(
        BoardRequestIdempotencyEntity claim,
        Func<Task<(int statusCode, string body, string? contentType)>> execute,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await execute();
            claim.ResponseStatusCode = outcome.statusCode;
            claim.ResponseBody = outcome.body;
            await db.SaveChangesAsync(cancellationToken);
            return outcome;
        }
        catch
        {
            await SafeRemoveClaimAsync(claim, idempotencyKey);
            throw;
        }
    }

    private async Task SafeRemoveClaimAsync(BoardRequestIdempotencyEntity claim, string idempotencyKey)
    {
        try
        {
            db.BoardRequestIdempotencies.Remove(claim);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove pending idempotency row {Key}.", idempotencyKey);
        }
    }

    private async Task WaitForOtherAsync(
        Guid userId,
        string idempotencyKey,
        string fingerprintHex,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < 400; i++)
        {
            await Task.Delay(25, cancellationToken);
            var row = await db.BoardRequestIdempotencies.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (row is null)
            {
                return;
            }

            if (!string.Equals(row.RequestFingerprintHex, fingerprintHex, StringComparison.OrdinalIgnoreCase))
            {
                throw new BoardIdempotencyFingerprintMismatchException();
            }

            if (row.ResponseStatusCode != PendingResponseCode)
            {
                return;
            }
        }

        logger.LogWarning("Idempotency wait timed out for user {UserId} key {Key}.", userId, idempotencyKey);
        throw new TimeoutException("Idempotency replay wait timed out.");
    }

    public static string IdempotencyMismatchJson() =>
        JsonSerializer.Serialize(
            new
            {
                problem = "idempotency_key_reuse",
                detail = "Idempotency-Key was reused with a different request fingerprint."
            },
            s_problemJson);
}

public sealed class BoardIdempotencyFingerprintMismatchException : Exception;
