using System.Net;
using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteBoardDataService : IBoardDataService
{
    private const string BoardCacheKey = "habitinator_board_cache_v1";
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;
    private readonly ILocalSettingsStore? _localStore;
    private readonly IActivityStatisticsReader? _statsReader;
    private BoardSnapshot? _cachedSnapshot;

    public RemoteBoardDataService(
        IHttpClientFactory http,
        ILocalSettingsStore? localStore = null,
        IActivityStatisticsReader? statsReader = null)
    {
        _http = http;
        _localStore = localStore;
        _statsReader = statsReader;
        if (_localStore != null)
        {
            var raw = _localStore.Read(BoardCacheKey);
            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    _cachedSnapshot = JsonSerializer.Deserialize<BoardSnapshot>(raw, Serializer);
                }
                catch
                {
                    // Ignore deserialization errors
                }
            }
        }
    }

    private HttpClient Client => _http.CreateClient("api");

    private static void AddMutationHeaders(HttpRequestMessage req, Guid operationId, DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (operationId != Guid.Empty)
        {
            req.Headers.TryAddWithoutValidation("Idempotency-Key", operationId.ToString("D"));
        }

        if (expectedUpdatedAtUtc is { } e)
        {
            req.Headers.TryAddWithoutValidation("X-Board-Expected-Updated-At-Utc", e.ToString("O"));
        }
    }

    private static async Task ThrowIfConflictAsync(HttpResponseMessage res, CancellationToken cancellationToken)
    {
        if (res.StatusCode != HttpStatusCode.Conflict)
        {
            return;
        }

        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        throw new BoardRemoteConflictException(body);
    }

    public async Task<BoardSyncDelta?> TryGetSyncDeltaAsync(string cursor, CancellationToken cancellationToken = default)
    {
        using var res = await Client.GetAsync(
            $"api/board/sync?cursor={Uri.EscapeDataString(cursor)}",
            cancellationToken);
        if (res.StatusCode == HttpStatusCode.BadRequest)
        {
            return null;
        }

        if (!res.IsSuccessStatusCode)
        {
            await ThrowIfConflictAsync(res, cancellationToken);
            res.EnsureSuccessStatusCode();
        }

        return await res.Content.ReadFromJsonAsync<BoardSyncDelta>(Serializer, cancellationToken);
    }

    public bool TryGetCachedSnapshot(out BoardSnapshot? snapshot)
    {
        snapshot = _cachedSnapshot;
        return snapshot is not null;
    }

    public async Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var res = await Client.GetAsync("api/board", cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                var hint = res.StatusCode == HttpStatusCode.Unauthorized
                    ? " Sign in again if you were logged out."
                    : " Is App.Web running? On Android emulator use 10.0.2.2 instead of 127.0.0.1 (set Api:BaseUrl or HABITINATOR_API_BASE_URL).";
                throw new InvalidOperationException($"Board request failed ({(int)res.StatusCode}).{hint}");
            }

            BoardSnapshot? s;
            try
            {
                s = await res.Content.ReadFromJsonAsync<BoardSnapshot>(Serializer, cancellationToken);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(
                    "Board response was not valid JSON. Check Api:BaseUrl / HABITINATOR_API_BASE_URL points at the Habitinator API host.");
            }

            _cachedSnapshot = s ?? throw new InvalidOperationException("Empty board response.");
            if (_localStore != null)
            {
                try
                {
                    _localStore.Write(BoardCacheKey, JsonSerializer.Serialize(_cachedSnapshot, Serializer));
                }
                catch
                {
                    // Ignore storage writing errors
                }
            }
            return _cachedSnapshot;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Could not reach the API. Start App.Web, then try again. On the Android emulator use base URL http://10.0.2.2:5033 because 127.0.0.1 is the emulator itself.",
                ex);
        }
    }

    public async Task<BoardItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var snap = await GetSnapshotAsync(cancellationToken);
        return snap.Habits.FirstOrDefault(x => x.Id == itemId)
            ?? snap.Dailies.FirstOrDefault(x => x.Id == itemId)
            ?? snap.Todos.FirstOrDefault(x => x.Id == itemId);
    }

    public async Task<Dictionary<Guid, int>> GetStreakMapAsync(CancellationToken cancellationToken = default)
    {
        using var res = await Client.GetAsync("api/board/streaks", cancellationToken);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Dictionary<Guid, int>>(Serializer, cancellationToken))
               ?? [];
    }

    public Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null,
        CancellationToken cancellationToken = default) =>
        CreateItemAsync(section, title, itemId, Guid.Empty, cancellationToken);

    public async Task<BoardItem> CreateItemAsync(
        BoardSection section,
        string title,
        Guid? itemId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"api/board/{section}")
        {
            Content = JsonContent.Create(new ItemTitleRequest(title, itemId), options: Serializer)
        };
        AddMutationHeaders(req, operationId, null);
        using var res = await Client.SendAsync(req, cancellationToken);
        await ThrowIfConflictAsync(res, cancellationToken);
        res.EnsureSuccessStatusCode();
        var item = await res.Content.ReadFromJsonAsync<BoardItem>(Serializer, cancellationToken)
               ?? throw new InvalidOperationException("Server returned an empty create response.");
        _statsReader?.InvalidateCache();
        return item;
    }

    public Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default) =>
        RenameItemAsync(section, itemId, title, Guid.Empty, null, cancellationToken);

    public async Task<BoardItem?> RenameItemAsync(
        BoardSection section,
        Guid itemId,
        string title,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Put, $"api/board/{section}/{itemId}")
        {
            Content = JsonContent.Create(new ItemTitleRequest(title), options: Serializer)
        };
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<bool> DeleteItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        DeleteItemAsync(section, itemId, Guid.Empty, null, cancellationToken);

    public async Task<bool> DeleteItemAsync(
        BoardSection section,
        Guid itemId,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Delete, $"api/board/{section}/{itemId}");
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await ThrowIfConflictAsync(res, cancellationToken);
        res.EnsureSuccessStatusCode();
        _statsReader?.InvalidateCache();
        return true;
    }

    public Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default) =>
        ArchiveItemAsync(section, itemId, Guid.Empty, null, cancellationToken);

    public async Task<BoardItem?> ArchiveItemAsync(
        BoardSection section,
        Guid itemId,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"api/board/{section}/{itemId}/archive");
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default) =>
        UnarchiveItemAsync(section, itemId, Guid.Empty, null, cancellationToken);

    public async Task<BoardItem?> UnarchiveItemAsync(
        BoardSection section,
        Guid itemId,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"api/board/{section}/{itemId}/unarchive");
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public async Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var res = await Client.GetAsync("api/board/archived", cancellationToken);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<BoardSnapshot>(Serializer, cancellationToken)
               ?? throw new InvalidOperationException("Server returned an empty snapshot response.");
    }

    public Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        ToggleItemAsync(section, itemId, Guid.Empty, null, cancellationToken);

    public async Task<BoardItem?> ToggleItemAsync(
        BoardSection section,
        Guid itemId,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"api/board/{section}/{itemId}/toggle");
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default) =>
        CompleteDailyForDateAsync(itemId, completedOn, Guid.Empty, null, cancellationToken);

    public async Task<BoardItem?> CompleteDailyForDateAsync(
        Guid itemId,
        DateOnly completedOn,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"api/board/dailies/{itemId}/complete-for-date")
        {
            Content = JsonContent.Create(new DailyCompleteForDateRequest(completedOn), options: Serializer)
        };
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        IncrementHabitPlusAsync(itemId, Guid.Empty, null, cancellationToken);

    public async Task<BoardItem?> IncrementHabitPlusAsync(
        Guid itemId,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"api/board/habits/{itemId}/increment");
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        IncrementHabitMinusAsync(itemId, Guid.Empty, null, cancellationToken);

    public async Task<BoardItem?> IncrementHabitMinusAsync(
        Guid itemId,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"api/board/habits/{itemId}/decrement");
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default) =>
        UpdateHabitAsync(
            itemId,
            args,
            Guid.Empty,
            null,
            cancellationToken);

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        HabitUpdateRequest body = new(
            args.Title,
            args.Notes,
            args.Tags,
            args.TrackPlus,
            args.TrackMinus,
            args.ResetPeriod,
            args.Counter,
            args.NegativeCounter,
            args.ChecklistJson,
            args.SortOrder);
        using HttpRequestMessage req = new(HttpMethod.Put, $"api/board/habits/{itemId}")
        {
            Content = JsonContent.Create(body, options: Serializer)
        };
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default) =>
        UpdateTodoAsync(
            itemId,
            args,
            Guid.Empty,
            null,
            cancellationToken);

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        TodoUpdateRequest body = new(
            args.Title,
            args.Notes,
            args.Tags,
            args.ChecklistJson,
            args.DueDate,
            args.SortOrder,
            args.TodoRepeatIntervalDays);
        using HttpRequestMessage req = new(HttpMethod.Put, $"api/board/todos/{itemId}")
        {
            Content = JsonContent.Create(body, options: Serializer)
        };
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default) =>
        UpdateDailyAsync(
            itemId,
            args,
            Guid.Empty,
            null,
            cancellationToken);

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DailyUpdateRequest body = new(
            args.Title,
            args.Notes,
            args.Tags,
            args.StartDate,
            args.Repeat,
            args.RepeatInterval,
            args.ChecklistJson,
            args.Counter,
            args.SortOrder);
        using HttpRequestMessage req = new(HttpMethod.Put, $"api/board/dailies/{itemId}")
        {
            Content = JsonContent.Create(body, options: Serializer)
        };
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    private async Task<BoardItem?> ReadBoardItemOrNullAsync(HttpResponseMessage res,
        CancellationToken cancellationToken)
    {
        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfConflictAsync(res, cancellationToken);
        res.EnsureSuccessStatusCode();
        var item = await res.Content.ReadFromJsonAsync<BoardItem>(Serializer, cancellationToken);
        if (item is not null)
        {
            _statsReader?.InvalidateCache();
        }
        return item;
    }
}
