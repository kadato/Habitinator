using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Services;

namespace App.Web;

internal static class BoardApiRoutes
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string JsonContentType = "application/json";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static void MapBoardApi(this WebApplication app)
    {
        RouteGroupBuilder boardApi = app.MapGroup("/api/board")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api");

        MapReadRoutes(boardApi);
        MapGeneralMutationRoutes(boardApi);
        MapHabitRoutes(boardApi);
        MapTodoRoutes(boardApi);
        MapDailyRoutes(boardApi);
    }

    private static void MapReadRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapGet("/",
            async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                BoardSnapshot snapshot = await boardPersistenceService.GetSnapshotAsync(userId);
                return Results.Json(snapshot, Json);
            });

        boardApi.MapGet("/sync",
            async (HttpRequest request, ClaimsPrincipal user, BoardPersistenceService boardPersistenceService,
                CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                string? cursorRaw = request.Query["cursor"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(cursorRaw))
                {
                    return Results.BadRequest(new { detail = "Query parameter 'cursor' is required (ISO 8601 watermark)." });
                }

                if (!DateTimeOffset.TryParse(cursorRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset cursor))
                {
                    return Results.BadRequest(new { detail = "Invalid cursor; expected ISO-8601 DateTimeOffset." });
                }

                BoardSyncDelta delta = await boardPersistenceService.GetSyncDeltaAsync(userId, cursor, cancellationToken);
                return Results.Json(delta, Json);
            });

        boardApi.MapGet("/archived",
            async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                BoardSnapshot snapshot = await boardPersistenceService.GetArchivedSnapshotAsync(userId);
                return Results.Json(snapshot, Json);
            });

        boardApi.MapGet("/streaks",
            async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService,
                CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                var streaks = await boardPersistenceService.GetDailyStreakMapAsync(userId, cancellationToken);
                return Results.Json(streaks, Json);
            });
    }

    private static void MapGeneralMutationRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapPost("/{section}", HandleCreateItemAsync);
        boardApi.MapPut("/{section}/{itemId:guid}", HandleRenameItemAsync);
        boardApi.MapPost("/{section}/{itemId:guid}/archive", HandleArchiveItemAsync);
        boardApi.MapPost("/{section}/{itemId:guid}/unarchive", HandleUnarchiveItemAsync);
        boardApi.MapDelete("/{section}/{itemId:guid}", HandleDeleteItemAsync);
        boardApi.MapPost("/{section}/{itemId:guid}/toggle", HandleToggleItemAsync);
    }

    private static async Task<IResult> HandleCreateItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, ItemTitleRequest request, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        return await RunIdempotentAsync(http, idem, userId, "POST", JsonSerializer.Serialize(request, Json), async ct =>
        {
            BoardItem item = await board.CreateItemAsync(
                userId, section, ZalgoSanitizer.SanitizeAndTrim(request.Title), request.ItemId, ct);
            return (200, JsonSerializer.Serialize(item, Json), JsonContentType);
        }, cancellationToken);
    }

    private static async Task<IResult> HandleRenameItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, ItemTitleRequest request, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        return await RunIdempotentAsync(http, idem, userId, "PUT", JsonSerializer.Serialize(request, Json), async ct =>
        {
            BoardMutationResult r = await board.RenameItemAsync(
                userId, section, itemId, ZalgoSanitizer.SanitizeAndTrim(request.Title), expected, ct);
            return MutationToOutcome(r);
        }, cancellationToken);
    }

    private static async Task<IResult> HandleArchiveItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        return await RunIdempotentAsync(http, idem, userId, "POST", "", async ct =>
        {
            BoardMutationResult r = await board.ArchiveItemAsync(userId, section, itemId, expected, ct);
            return MutationToOutcome(r);
        }, cancellationToken);
    }

    private static async Task<IResult> HandleUnarchiveItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        return await RunIdempotentAsync(http, idem, userId, "POST", "", async ct =>
        {
            BoardMutationResult r = await board.UnarchiveItemAsync(userId, section, itemId, expected, ct);
            return MutationToOutcome(r);
        }, cancellationToken);
    }

    private static async Task<IResult> HandleDeleteItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        return await RunIdempotentAsync(http, idem, userId, "DELETE", "", async ct =>
        {
            BoardMutationResult r = await board.DeleteItemAsync(userId, section, itemId, expected, ct);
            return r.Status switch
            {
                BoardMutationStatus.Ok => (204, "", null),
                BoardMutationStatus.NotFound => (404, "", null),
                BoardMutationStatus.Conflict => (
                    409,
                    JsonSerializer.Serialize(new { problem = "version_conflict", item = r.Item }, Json),
                    JsonContentType),
                _ => (500, "{}", JsonContentType)
            };
        }, cancellationToken);
    }

    private static async Task<IResult> HandleToggleItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        return await RunIdempotentAsync(http, idem, userId, "POST", "", async ct =>
        {
            BoardMutationResult r = await board.ToggleItemAsync(userId, section, itemId, expected, ct);
            return MutationToOutcome(r);
        }, cancellationToken);
    }

    private static void MapHabitRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapPost("/habits/{itemId:guid}/increment",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                return await RunIdempotentAsync(http, idem, userId, "POST", "", async ct =>
                {
                    BoardMutationResult r = await board.IncrementHabitPlusAsync(userId, itemId, expected, ct);
                    return MutationToOutcome(r);
                }, cancellationToken);
            });

        boardApi.MapPost("/habits/{itemId:guid}/decrement",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                return await RunIdempotentAsync(http, idem, userId, "POST", "", async ct =>
                {
                    BoardMutationResult r = await board.IncrementHabitMinusAsync(userId, itemId, expected, ct);
                    return MutationToOutcome(r);
                }, cancellationToken);
            });

        boardApi.MapPut("/habits/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, HabitUpdateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                return await RunIdempotentAsync(http, idem, userId, "PUT", JsonSerializer.Serialize(request, Json), async ct =>
                {
                    BoardMutationResult r = await board.UpdateHabitAsync(
                        userId,
                        itemId,
                        new UpdateHabitArgs(
                            ZalgoSanitizer.SanitizeAndTrim(request.Title),
                            ZalgoSanitizer.Sanitize(request.Notes),
                            ZalgoSanitizer.Sanitize(request.Tags),
                            request.TrackPlus,
                            request.TrackMinus,
                            request.ResetPeriod,
                            request.Counter,
                            request.NegativeCounter,
                            DailyChecklistJson.Serialize(DailyChecklistJson.Parse(request.ChecklistJson)),
                            request.SortOrder,
                            expected),
                        ct);
                    return MutationToOutcome(r);
                }, cancellationToken);
            });
    }

    private static void MapTodoRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapPut("/todos/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, TodoUpdateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                return await RunIdempotentAsync(http, idem, userId, "PUT", JsonSerializer.Serialize(request, Json), async ct =>
                {
                    BoardMutationResult r = await board.UpdateTodoAsync(
                        userId,
                        itemId,
                        new UpdateTodoArgs(
                            ZalgoSanitizer.SanitizeAndTrim(request.Title),
                            ZalgoSanitizer.Sanitize(request.Notes),
                            ZalgoSanitizer.Sanitize(request.Tags),
                            DailyChecklistJson.Serialize(DailyChecklistJson.Parse(request.ChecklistJson)),
                            request.DueDate,
                            request.SortOrder,
                            expected),
                        ct);
                    return MutationToOutcome(r);
                }, cancellationToken);
            });
    }

    private static void MapDailyRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapPut("/dailies/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, DailyUpdateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                return await RunIdempotentAsync(http, idem, userId, "PUT", JsonSerializer.Serialize(request, Json), async ct =>
                {
                    BoardMutationResult r = await board.UpdateDailyAsync(
                        userId,
                        itemId,
                        new UpdateDailyArgs(
                            ZalgoSanitizer.SanitizeAndTrim(request.Title),
                            ZalgoSanitizer.Sanitize(request.Notes),
                            ZalgoSanitizer.Sanitize(request.Tags),
                            request.StartDate,
                            request.Repeat,
                            request.RepeatInterval,
                            DailyChecklistJson.Serialize(DailyChecklistJson.Parse(request.ChecklistJson)),
                            request.Streak,
                            request.SortOrder,
                            expected),
                        ct);
                    return MutationToOutcome(r);
                }, cancellationToken);
            });

        boardApi.MapPost("/dailies/{itemId:guid}/complete-for-date",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, DailyCompleteForDateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                return await RunIdempotentAsync(http, idem, userId, "POST", JsonSerializer.Serialize(request, Json), async ct =>
                {
                    BoardMutationResult r = await board.CompleteDailyForDateAsync(
                        userId, itemId, request.CompletedOn, expected, ct);
                    return MutationToOutcome(r);
                }, cancellationToken);
            });
    }

    /// <summary>Runs a board mutation with the shared idempotency + optimistic-concurrency envelope.</summary>
    private static async Task<IResult> RunIdempotentAsync(
        HttpContext http,
        BoardIdempotencyService idem,
        Guid userId,
        string method,
        string bodyJson,
        Func<CancellationToken, Task<(int statusCode, string body, string? contentType)>> execute,
        CancellationToken cancellationToken)
    {
        string path = http.Request.Path.Value ?? "";
        try
        {
            (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                userId,
                http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                BoardIdempotencyService.ComputeFingerprintHex(method, path, bodyJson),
                () => execute(cancellationToken),
                cancellationToken);
            return ToHttpResult(outcome);
        }
        catch (BoardIdempotencyFingerprintMismatchException)
        {
            return Results.Text(
                BoardIdempotencyService.IdempotencyMismatchJson(),
                JsonContentType,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static DateTimeOffset? ReadExpectedUpdatedAtUtc(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Board-Expected-Updated-At-Utc", out Microsoft.Extensions.Primitives.StringValues custom))
        {
            string s = custom.ToString();
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset d))
            {
                return d;
            }
        }

        if (request.Headers.TryGetValue("If-Match", out Microsoft.Extensions.Primitives.StringValues etag))
        {
            string raw = etag.ToString().Trim();
            if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length > 1)
            {
                raw = raw[1..^1];
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset d2))
            {
                return d2;
            }
        }

        return null;
    }

    private static (int statusCode, string body, string? contentType) MutationToOutcome(BoardMutationResult r) =>
        r.Status switch
        {
            BoardMutationStatus.Ok when r.Item is not null => (
                200,
                JsonSerializer.Serialize(r.Item, Json),
                JsonContentType),
            BoardMutationStatus.Ok => (204, "", null),
            BoardMutationStatus.NotFound => (404, "", null),
            BoardMutationStatus.Conflict => (
                409,
                JsonSerializer.Serialize(new { problem = "version_conflict", item = r.Item }, Json),
                JsonContentType),
            _ => (500, "{}", JsonContentType)
        };

    private static IResult ToHttpResult((int statusCode, string body, string? contentType) o)
    {
        if (o.statusCode == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        if (o.statusCode == StatusCodes.Status404NotFound)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrEmpty(o.body))
        {
            return Results.StatusCode(o.statusCode);
        }

        return Results.Text(o.body, o.contentType ?? JsonContentType, statusCode: o.statusCode);
    }
}
