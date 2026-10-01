using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using App.Web.Data;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace App.Web.Services;

/// <summary>Idempotent replay for board mutations, using the Idempotency-Key and request fingerprint.</summary>
public sealed class BoardIdempotencyService(
    ApplicationDbContext db,
    ILogger<BoardIdempotencyService> logger)
{
    public const int PendingResponseCode = -1;

    /// <summary>Matches the <c>varchar(128)</c> column. Longer keys are rejected before they reach the database.</summary>
    public const int MaxIdempotencyKeyLength = 128;

    private const int MaxClaimAttempts = 5;

    private const int MaxWaitMilliseconds = 500;

    private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(10);

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

        var claimAttempts = 0;
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
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                db.Entry(claim).State = EntityState.Detached; // Detach failed entry from change tracker
                claimAttempts++;
                if (claimAttempts >= MaxClaimAttempts)
                {
                    logger.LogError(
                        ex,
                        "Could not claim idempotency key for user {UserId} after {Attempts} attempts.",
                        userId,
                        claimAttempts);
                    throw;
                }

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
        var delayMs = 25;
        var deadline = DateTimeOffset.UtcNow + s_waitTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(delayMs, cancellationToken);
            delayMs = Math.Min(delayMs * 2, MaxWaitMilliseconds);

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

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException { SqlState: "23505" })
            {
                return true;
            }
        }

        return false;
    }

    public static string IdempotencyMismatchJson() =>
        JsonSerializer.Serialize(
            new
            {
                problem = "idempotency_key_reuse",
                detail = "Idempotency-Key was reused with a different request fingerprint."
            },
            s_problemJson);

    public static string IdempotencyKeyTooLongJson() =>
        JsonSerializer.Serialize(
            new
            {
                problem = "idempotency_key_too_long",
                detail = $"Idempotency-Key must be at most {MaxIdempotencyKeyLength} characters."
            },
            s_problemJson);
}

public sealed class BoardIdempotencyFingerprintMismatchException : Exception;
