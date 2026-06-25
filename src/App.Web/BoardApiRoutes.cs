using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL;
using App.Shared.RCL.Models;
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

        string path = http.Request.Path.Value ?? "";
        string bodyJson = JsonSerializer.Serialize(request, Json);
        try
        {
            (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                userId,
                http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                BoardIdempotencyService.ComputeFingerprintHex("POST", path, bodyJson),
                async () =>
                {
                    BoardItem item = await board.CreateItemAsync(userId, section, ZalgoSanitizer.SanitizeAndTrim(request.Title), request.ItemId, cancellationToken);
                    return (200, JsonSerializer.Serialize(item, Json), JsonContentType);
                },
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

    private static async Task<IResult> HandleRenameItemAsync(
        HttpContext http, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, ItemTitleRequest request, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(http.User) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        string path = http.Request.Path.Value ?? "";
        string bodyJson = JsonSerializer.Serialize(request, Json);
        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        try
        {
            (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                userId,
                http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                BoardIdempotencyService.ComputeFingerprintHex("PUT", path, bodyJson),
                async () =>
                {
                    BoardMutationResult r = await board.RenameItemForApiAsync(
                        userId,
                        section,
                        itemId,
                        ZalgoSanitizer.SanitizeAndTrim(request.Title),
                        expected,
                        cancellationToken);
                    return MutationToOutcome(r);
                },
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

    private static async Task<IResult> HandleArchiveItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        string path = http.Request.Path.Value ?? "";
        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        try
        {
            (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                userId,
                http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                async () =>
                {
                    BoardMutationResult r = await board.ArchiveItemForApiAsync(userId, section, itemId, expected, cancellationToken);
                    return MutationToOutcome(r);
                },
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

    private static async Task<IResult> HandleUnarchiveItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        string path = http.Request.Path.Value ?? "";
        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        try
        {
            (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                userId,
                http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                async () =>
                {
                    BoardMutationResult r = await board.UnarchiveItemForApiAsync(userId, section, itemId, expected, cancellationToken);
                    return MutationToOutcome(r);
                },
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

    private static async Task<IResult> HandleDeleteItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        string path = http.Request.Path.Value ?? "";
        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        try
        {
            (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                userId,
                http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                BoardIdempotencyService.ComputeFingerprintHex("DELETE", path, ""),
                async () =>
                {
                    BoardMutationResult r = await board.DeleteItemForApiAsync(userId, section, itemId, expected, cancellationToken);
                    return r.Status switch
                    {
                        BoardMutationStatus.Ok => (204, "", (string?)null),
                        BoardMutationStatus.NotFound => (404, "", (string?)null),
                        BoardMutationStatus.Conflict => (
                            409,
                            JsonSerializer.Serialize(new { problem = "version_conflict", item = r.Item }, Json),
                            JsonContentType),
                        _ => (500, "{}", JsonContentType)
                    };
                },
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

    private static async Task<IResult> HandleToggleItemAsync(
        HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
        BoardSection section, Guid itemId, CancellationToken cancellationToken)
    {
        if (AuthenticatedUserId.TryGet(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        string path = http.Request.Path.Value ?? "";
        DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
        try
        {
            (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                userId,
                http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                async () =>
                {
                    BoardMutationResult r = await board.ToggleItemForApiAsync(userId, section, itemId, expected, cancellationToken);
                    return MutationToOutcome(r);
                },
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

                string path = http.Request.Path.Value ?? "";
                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                        async () =>
                        {
                            BoardMutationResult r = await board.IncrementHabitPlusForApiAsync(userId, itemId, expected, cancellationToken);
                            return MutationToOutcome(r);
                        },
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
            });

        boardApi.MapPost("/habits/{itemId:guid}/decrement",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                string path = http.Request.Path.Value ?? "";
                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                        async () =>
                        {
                            BoardMutationResult r = await board.IncrementHabitMinusForApiAsync(userId, itemId, expected, cancellationToken);
                            return MutationToOutcome(r);
                        },
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
            });

        boardApi.MapPut("/habits/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, HabitUpdateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                string path = http.Request.Path.Value ?? "";
                string bodyJson = JsonSerializer.Serialize(request, Json);
                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("PUT", path, bodyJson),
                        async () =>
                        {
                            BoardMutationResult r = await board.UpdateHabitForApiAsync(
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
                                cancellationToken);
                            return MutationToOutcome(r);
                        },
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

                string path = http.Request.Path.Value ?? "";
                string bodyJson = JsonSerializer.Serialize(request, Json);
                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("PUT", path, bodyJson),
                        async () =>
                        {
                            BoardMutationResult r = await board.UpdateTodoForApiAsync(
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
                                cancellationToken);
                            return MutationToOutcome(r);
                        },
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

                string path = http.Request.Path.Value ?? "";
                string bodyJson = JsonSerializer.Serialize(request, Json);
                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("PUT", path, bodyJson),
                        async () =>
                        {
                            BoardMutationResult r = await board.UpdateDailyForApiAsync(
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
                                cancellationToken);
                            return MutationToOutcome(r);
                        },
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
            });

        boardApi.MapPost("/dailies/{itemId:guid}/complete-for-date",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, DailyCompleteForDateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                string path = http.Request.Path.Value ?? "";
                string bodyJson = JsonSerializer.Serialize(request, Json);
                DateTimeOffset? expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    (int statusCode, string body, string? contentType) outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers[IdempotencyKeyHeader].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, bodyJson),
                        async () =>
                        {
                            BoardMutationResult r = await board.CompleteDailyForDateForApiAsync(
                                userId,
                                itemId,
                                request.CompletedOn,
                                expected,
                                cancellationToken);
                            return MutationToOutcome(r);
                        },
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
            });
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

