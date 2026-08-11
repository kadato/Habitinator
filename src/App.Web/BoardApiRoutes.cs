using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

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

    internal static JsonSerializerOptions Json => JsonDefaults.Api;

    internal static void MapBoardApi(this WebApplication app)
    {
        var boardApi = app.MapGroup("/api/board")
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
                if (ResolveUser(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                var snapshot = await boardPersistenceService.GetSnapshotAsync(userId);
                return Results.Json(snapshot, Json);
            });

        boardApi.MapGet("/sync",
            async (HttpRequest request, ClaimsPrincipal user, BoardPersistenceService boardPersistenceService,
                CancellationToken cancellationToken) =>
            {
                if (ResolveUser(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                var cursorRaw = request.Query["cursor"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(cursorRaw))
                {
                    return Results.BadRequest(new { detail = "Query parameter 'cursor' is required (ISO 8601 watermark)." });
                }

                if (!DateTimeOffset.TryParse(cursorRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var cursor))
                {
                    return Results.BadRequest(new { detail = "Invalid cursor; expected ISO-8601 DateTimeOffset." });
                }

                var delta = await boardPersistenceService.GetSyncDeltaAsync(userId, cursor, cancellationToken);
                return Results.Json(delta, Json);
            });

        boardApi.MapGet("/archived",
            async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService) =>
            {
                if (ResolveUser(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                var snapshot = await boardPersistenceService.GetArchivedSnapshotAsync(userId);
                return Results.Json(snapshot, Json);
            });

        boardApi.MapGet("/streaks",
            async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService,
                CancellationToken cancellationToken) =>
            {
                if (ResolveUser(user) is not Guid userId)
                {
                    return Results.Unauthorized();
                }

                var streaks = await boardPersistenceService.GetDailyStreakMapAsync(userId, cancellationToken);
                return Results.Json(streaks, Json);
            });
    }

    private static Guid? ResolveUser(ClaimsPrincipal user) => AuthenticatedUserId.TryGet(user);

    private static void MapGeneralMutationRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapPost("/{section}", HandleCreateItemAsync);
        boardApi.MapPut("/{section}/{itemId:guid}", HandleRenameItemAsync);
        boardApi.MapPost("/{section}/{itemId:guid}/archive", HandleArchiveItemAsync);
        boardApi.MapPost("/{section}/{itemId:guid}/unarchive", HandleUnarchiveItemAsync);
        boardApi.MapDelete("/{section}/{itemId:guid}", HandleDeleteItemAsync);
        boardApi.MapPost("/{section}/{itemId:guid}/toggle", HandleToggleItemAsync);
    }

    private sealed record BoardMutationContext(
        HttpContext Http,
        BoardPersistenceService Board,
        BoardIdempotencyService Idem,
        CancellationToken CancellationToken);

    private static async Task<IResult> HandleCreateItemAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        BoardSection section,
        ItemTitleRequest request)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "POST", JsonSerializer.Serialize(request, Json), async ct =>
        {
            var item = await ctx.Board.CreateItemAsync(
                userId, section, ZalgoSanitizer.SanitizeAndTrim(request.Title), request.ItemId, ct);
            return (200, JsonSerializer.Serialize(item, Json), JsonContentType);
        }, ctx.CancellationToken);
    }

    private static async Task<IResult> HandleRenameItemAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        BoardSection section,
        Guid itemId,
        ItemTitleRequest request)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "PUT", JsonSerializer.Serialize(request, Json), async ct =>
        {
            var r = await ctx.Board.RenameItemAsync(
                userId, section, itemId, ZalgoSanitizer.SanitizeAndTrim(request.Title), expected, ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
    }

    private static async Task<IResult> HandleArchiveItemAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        BoardSection section,
        Guid itemId)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "POST", "", async ct =>
        {
            var r = await ctx.Board.ArchiveItemAsync(userId, section, itemId, expected, ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
    }

    private static async Task<IResult> HandleUnarchiveItemAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        BoardSection section,
        Guid itemId)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "POST", "", async ct =>
        {
            var r = await ctx.Board.UnarchiveItemAsync(userId, section, itemId, expected, ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
    }

    private static async Task<IResult> HandleDeleteItemAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        BoardSection section,
        Guid itemId)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "DELETE", "", async ct =>
        {
            var r = await ctx.Board.DeleteItemAsync(userId, section, itemId, expected, ct);
            return MutationToOutcome(r, okStatus: StatusCodes.Status204NoContent);
        }, ctx.CancellationToken);
    }

    private static async Task<IResult> HandleToggleItemAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        BoardSection section,
        Guid itemId)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "POST", "", async ct =>
        {
            var r = await ctx.Board.ToggleItemAsync(userId, section, itemId, expected, ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
    }

    private static void MapHabitRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapPost("/habits/{itemId:guid}/increment", HandleIncrementHabitAsync);
        boardApi.MapPost("/habits/{itemId:guid}/decrement", HandleDecrementHabitAsync);
        boardApi.MapPut("/habits/{itemId:guid}", HandleUpdateHabitAsync);
    }

    private static async Task<IResult> HandleIncrementHabitAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        Guid itemId)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "POST", "", async ct =>
        {
            var r = await ctx.Board.IncrementHabitPlusAsync(userId, itemId, expected, ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
    }

    private static async Task<IResult> HandleDecrementHabitAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        Guid itemId)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "POST", "", async ct =>
        {
            var r = await ctx.Board.IncrementHabitMinusAsync(userId, itemId, expected, ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
    }

    private static async Task<IResult> HandleUpdateHabitAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        Guid itemId,
        HabitUpdateRequest request)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "PUT", JsonSerializer.Serialize(request, Json), async ct =>
        {
            var r = await ctx.Board.UpdateHabitAsync(
                userId,
                itemId,
                new UpdateHabitArgs(
                    request.Title,
                    request.Notes,
                    request.Tags,
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
        }, ctx.CancellationToken);
    }

    private static void MapTodoRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapPut("/todos/{itemId:guid}", HandleUpdateTodoAsync);
    }

    private static async Task<IResult> HandleUpdateTodoAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        Guid itemId,
        TodoUpdateRequest request)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "PUT", JsonSerializer.Serialize(request, Json), async ct =>
        {
            var r = await ctx.Board.UpdateTodoAsync(
                userId,
                itemId,
                new UpdateTodoArgs(
                    request.Title,
                    request.Notes,
                    request.Tags,
                    DailyChecklistJson.Serialize(DailyChecklistJson.Parse(request.ChecklistJson)),
                    request.DueDate,
                    request.SortOrder,
                    request.TodoRepeatIntervalDays,
                    expected),
                ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
    }

    private static void MapDailyRoutes(RouteGroupBuilder boardApi)
    {
        boardApi.MapPut("/dailies/{itemId:guid}", HandleUpdateDailyAsync);
        boardApi.MapPost("/dailies/{itemId:guid}/complete-for-date", HandleCompleteDailyForDateAsync);
    }

    private static async Task<IResult> HandleUpdateDailyAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        Guid itemId,
        DailyUpdateRequest request)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "PUT", JsonSerializer.Serialize(request, Json), async ct =>
        {
            var r = await ctx.Board.UpdateDailyAsync(
                userId,
                itemId,
                new UpdateDailyArgs(
                    request.Title,
                    request.Notes,
                    request.Tags,
                    request.StartDate,
                    request.Repeat,
                    request.RepeatInterval,
                    DailyChecklistJson.Serialize(DailyChecklistJson.Parse(request.ChecklistJson)),
                    request.Counter,
                    request.SortOrder,
                    expected),
                ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
    }

    private static async Task<IResult> HandleCompleteDailyForDateAsync(
        [AsParameters] BoardMutationContext ctx,
        ClaimsPrincipal user,
        Guid itemId,
        DailyCompleteForDateRequest request)
    {
        if (ResolveUser(user) is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var expected = ReadExpectedUpdatedAtUtc(ctx.Http.Request);
        return await RunIdempotentAsync(ctx.Http, ctx.Idem, userId, "POST", JsonSerializer.Serialize(request, Json), async ct =>
        {
            var r = await ctx.Board.CompleteDailyForDateAsync(
                userId, itemId, request.CompletedOn, expected, ct);
            return MutationToOutcome(r);
        }, ctx.CancellationToken);
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
        var path = http.Request.Path.Value ?? "";
        try
        {
            var outcome = await idem.RunAsync(
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
        if (request.Headers.TryGetValue("X-Board-Expected-Updated-At-Utc", out var custom))
        {
            var s = custom.ToString();
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return d;
            }
        }

        if (request.Headers.TryGetValue("If-Match", out var etag))
        {
            var raw = etag.ToString().Trim();
            if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length > 1)
            {
                raw = raw[1..^1];
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
            {
                return d2;
            }
        }

        return null;
    }

    private static (int statusCode, string body, string? contentType) MutationToOutcome(BoardMutationResult r, int okStatus = 200) =>
        r.Status switch
        {
            BoardMutationStatus.Ok when r.Item is not null => (
                okStatus,
                JsonSerializer.Serialize(r.Item, Json),
                JsonContentType),
            BoardMutationStatus.Ok => (okStatus, "", null),
            BoardMutationStatus.NotFound => (404, "", null),
            BoardMutationStatus.Conflict => (
                409,
                JsonSerializer.Serialize(new { problem = "version_conflict", item = r.Item }, Json),
                JsonContentType),
            _ => (
                500,
                JsonSerializer.Serialize(new { detail = "Unexpected board mutation status." }, Json),
                JsonContentType)
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
