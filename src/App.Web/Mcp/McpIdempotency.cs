using System.Text.Json;

using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;

namespace App.Web.Mcp;

/// <summary>
/// Idempotent execution for MCP create tools, backed by the
/// <see cref="BoardIdempotencyService"/> store.
/// Reusing a key with identical fields replays the stored result.
/// Reusing it with different fields fails.
/// </summary>
internal static class McpIdempotency
{
    public static async Task<T> ExecuteAsync<T>(
        BoardIdempotencyService idem,
        Guid userId,
        string? idempotencyKey,
        string toolName,
        object fingerprintPayload,
        Func<CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return await execute(cancellationToken);
        }

        McpInput.CheckIdempotencyKey(idempotencyKey);
        var fingerprint = BoardIdempotencyService.ComputeFingerprintHex(
            "MCP", toolName, JsonSerializer.Serialize(fingerprintPayload, JsonDefaults.Api));

        T? live = null;
        try
        {
            var (_, body, _) = await idem.RunAsync(
                userId,
                idempotencyKey,
                fingerprint,
                async () =>
                {
                    live = await execute(cancellationToken);
                    return (StatusCodes.Status200OK, JsonSerializer.Serialize(live, JsonDefaults.Api), "application/json");
                },
                cancellationToken);

            // Fresh execution returns the live object; a replay deserializes the
            // stored body. Both paths return the same shape.
            if (live is not null)
            {
                return live;
            }

            return JsonSerializer.Deserialize<T>(body, JsonDefaults.Api)
                ?? throw new McpProtocolException(
                    $"Idempotent replay for key '{idempotencyKey}' returned no result. Retry with a new key.",
                    McpErrorCode.InternalError);
        }
        catch (BoardIdempotencyFingerprintMismatchException)
        {
            throw new McpProtocolException(
                $"idempotencyKey: key '{idempotencyKey}' was already used with different fields. " +
                "Reusing a key on retry returns the original item only when every field matches; " +
                "otherwise pass a new idempotencyKey.",
                McpErrorCode.InvalidRequest);
        }
    }
}
