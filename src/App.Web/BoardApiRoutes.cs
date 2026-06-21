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
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static void MapBoardApi(this WebApplication app)
    {
        var boardApi = app.MapGroup("/api/board")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api");

        boardApi.MapGet("/",
            async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
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
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var cursorRaw = request.Query["cursor"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(cursorRaw))
                {
                    return Results.BadRequest(new { detail = "Query parameter 'cursor' is required (ISO 8601 watermark)." });
                }

                if (!DateTimeOffset.TryParse(cursorRaw, out var cursor))
                {
                    return Results.BadRequest(new { detail = "Invalid cursor; expected ISO-8601 DateTimeOffset." });
                }

                var delta = await boardPersistenceService.GetSyncDeltaAsync(userId, cursor, cancellationToken);
                return Results.Json(delta, Json);
            });

        boardApi.MapPost("/{section}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                BoardSection section, ItemTitleRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var bodyJson = JsonSerializer.Serialize(request, Json);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, bodyJson),
                        async () =>
                        {
                            var item = await board.CreateItemAsync(userId, section, ZalgoSanitizer.SanitizeAndTrim(request.Title), request.ItemId, cancellationToken);
                            return (200, JsonSerializer.Serialize(item, Json), "application/json");
                        },
                        cancellationToken);
                    return ToHttpResult(outcome);
                }
                catch (BoardIdempotencyFingerprintMismatchException)
                {
                    return Results.Text(
                        BoardIdempotencyService.IdempotencyMismatchJson(),
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPut("/{section}/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                BoardSection section, Guid itemId, ItemTitleRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var bodyJson = JsonSerializer.Serialize(request, Json);
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("PUT", path, bodyJson),
                        async () =>
                        {
                            var r = await board.RenameItemForApiAsync(
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
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapGet("/archived",
            async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var snapshot = await boardPersistenceService.GetArchivedSnapshotAsync(userId);
                return Results.Json(snapshot, Json);
            });

        boardApi.MapPost("/{section}/{itemId:guid}/archive",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                BoardSection section, Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                        async () =>
                        {
                            var r = await board.ArchiveItemForApiAsync(userId, section, itemId, expected, cancellationToken);
                            return MutationToOutcome(r);
                        },
                        cancellationToken);
                    return ToHttpResult(outcome);
                }
                catch (BoardIdempotencyFingerprintMismatchException)
                {
                    return Results.Text(
                        BoardIdempotencyService.IdempotencyMismatchJson(),
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPost("/{section}/{itemId:guid}/unarchive",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                BoardSection section, Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                        async () =>
                        {
                            var r = await board.UnarchiveItemForApiAsync(userId, section, itemId, expected, cancellationToken);
                            return MutationToOutcome(r);
                        },
                        cancellationToken);
                    return ToHttpResult(outcome);
                }
                catch (BoardIdempotencyFingerprintMismatchException)
                {
                    return Results.Text(
                        BoardIdempotencyService.IdempotencyMismatchJson(),
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapDelete("/{section}/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                BoardSection section, Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("DELETE", path, ""),
                        async () =>
                        {
                            var r = await board.DeleteItemForApiAsync(userId, section, itemId, expected, cancellationToken);
                            return r.Status switch
                            {
                                BoardMutationStatus.Ok => (204, "", (string?)null),
                                BoardMutationStatus.NotFound => (404, "", (string?)null),
                                BoardMutationStatus.Conflict => (
                                    409,
                                    JsonSerializer.Serialize(new { problem = "version_conflict", item = r.Item }, Json),
                                    "application/json"),
                                _ => (500, "{}", "application/json")
                            };
                        },
                        cancellationToken);
                    return ToHttpResult(outcome);
                }
                catch (BoardIdempotencyFingerprintMismatchException)
                {
                    return Results.Text(
                        BoardIdempotencyService.IdempotencyMismatchJson(),
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPost("/{section}/{itemId:guid}/toggle",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                BoardSection section, Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                        async () =>
                        {
                            var r = await board.ToggleItemForApiAsync(userId, section, itemId, expected, cancellationToken);
                            return MutationToOutcome(r);
                        },
                        cancellationToken);
                    return ToHttpResult(outcome);
                }
                catch (BoardIdempotencyFingerprintMismatchException)
                {
                    return Results.Text(
                        BoardIdempotencyService.IdempotencyMismatchJson(),
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPost("/habits/{itemId:guid}/increment",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                        async () =>
                        {
                            var r = await board.IncrementHabitPlusForApiAsync(userId, itemId, expected, cancellationToken);
                            return MutationToOutcome(r);
                        },
                        cancellationToken);
                    return ToHttpResult(outcome);
                }
                catch (BoardIdempotencyFingerprintMismatchException)
                {
                    return Results.Text(
                        BoardIdempotencyService.IdempotencyMismatchJson(),
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPost("/habits/{itemId:guid}/decrement",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, ""),
                        async () =>
                        {
                            var r = await board.IncrementHabitMinusForApiAsync(userId, itemId, expected, cancellationToken);
                            return MutationToOutcome(r);
                        },
                        cancellationToken);
                    return ToHttpResult(outcome);
                }
                catch (BoardIdempotencyFingerprintMismatchException)
                {
                    return Results.Text(
                        BoardIdempotencyService.IdempotencyMismatchJson(),
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPut("/habits/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, HabitUpdateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var bodyJson = JsonSerializer.Serialize(request, Json);
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("PUT", path, bodyJson),
                        async () =>
                        {
                            var r = await board.UpdateHabitForApiAsync(
                                userId,
                                itemId,
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
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPut("/todos/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, TodoUpdateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var bodyJson = JsonSerializer.Serialize(request, Json);
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("PUT", path, bodyJson),
                        async () =>
                        {
                            var r = await board.UpdateTodoForApiAsync(
                                userId,
                                itemId,
                                ZalgoSanitizer.SanitizeAndTrim(request.Title),
                                ZalgoSanitizer.Sanitize(request.Notes),
                                ZalgoSanitizer.Sanitize(request.Tags),
                                DailyChecklistJson.Serialize(DailyChecklistJson.Parse(request.ChecklistJson)),
                                request.DueDate,
                                request.SortOrder,
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
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPut("/dailies/{itemId:guid}",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, DailyUpdateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var bodyJson = JsonSerializer.Serialize(request, Json);
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("PUT", path, bodyJson),
                        async () =>
                        {
                            var r = await board.UpdateDailyForApiAsync(
                                userId,
                                itemId,
                                ZalgoSanitizer.SanitizeAndTrim(request.Title),
                                ZalgoSanitizer.Sanitize(request.Notes),
                                ZalgoSanitizer.Sanitize(request.Tags),
                                request.StartDate,
                                request.Repeat,
                                request.RepeatInterval,
                                DailyChecklistJson.Serialize(DailyChecklistJson.Parse(request.ChecklistJson)),
                                request.Streak,
                                request.SortOrder,
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
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });

        boardApi.MapPost("/dailies/{itemId:guid}/complete-for-date",
            async (HttpContext http, ClaimsPrincipal user, BoardPersistenceService board, BoardIdempotencyService idem,
                Guid itemId, DailyCompleteForDateRequest request, CancellationToken cancellationToken) =>
            {
                if (AuthenticatedUserId.TryGet(user) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var path = http.Request.Path.Value ?? "";
                var bodyJson = JsonSerializer.Serialize(request, Json);
                var expected = ReadExpectedUpdatedAtUtc(http.Request);
                try
                {
                    var outcome = await idem.RunAsync(
                        userId,
                        http.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                        BoardIdempotencyService.ComputeFingerprintHex("POST", path, bodyJson),
                        async () =>
                        {
                            var r = await board.CompleteDailyForDateForApiAsync(
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
                        "application/json",
                        statusCode: StatusCodes.Status409Conflict);
                }
            });
    }

    private static DateTimeOffset? ReadExpectedUpdatedAtUtc(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Board-Expected-Updated-At-Utc", out var custom))
        {
            var s = custom.ToString();
            if (DateTimeOffset.TryParse(s, out var d))
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

            if (DateTimeOffset.TryParse(raw, out var d2))
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
                "application/json"),
            BoardMutationStatus.Ok => (204, "", null),
            BoardMutationStatus.NotFound => (404, "", null),
            BoardMutationStatus.Conflict => (
                409,
                JsonSerializer.Serialize(new { problem = "version_conflict", item = r.Item }, Json),
                "application/json"),
            _ => (500, "{}", "application/json")
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

        return Results.Text(o.body, o.contentType ?? "application/json", statusCode: o.statusCode);
    }
}
