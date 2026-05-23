using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.MAUI.Services;

public sealed class RemoteBoardDataService : IBoardDataService
{
    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _http;

    public RemoteBoardDataService(IHttpClientFactory http)
    {
        _http = http;
    }

    private HttpClient Client => _http.CreateClient("api");

    private static void AddMutationHeaders(HttpRequestMessage req, Guid operationId, DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (operationId != Guid.Empty)
            req.Headers.TryAddWithoutValidation("Idempotency-Key", operationId.ToString("D"));
        if (expectedUpdatedAtUtc is { } e)
            req.Headers.TryAddWithoutValidation("X-Board-Expected-Updated-At-Utc", e.ToString("O"));
    }

    private static async Task ThrowIfConflictAsync(HttpResponseMessage res, CancellationToken cancellationToken)
    {
        if (res.StatusCode != HttpStatusCode.Conflict) return;

        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        throw new BoardRemoteConflictException(body);
    }

    public async Task<BoardSyncDelta?> TryGetSyncDeltaAsync(string cursor, CancellationToken cancellationToken = default)
    {
        using var res = await Client.GetAsync(
            $"api/board/sync?cursor={Uri.EscapeDataString(cursor)}",
            cancellationToken);
        if (res.StatusCode == HttpStatusCode.BadRequest) return null;

        if (!res.IsSuccessStatusCode)
        {
            await ThrowIfConflictAsync(res, cancellationToken);
            res.EnsureSuccessStatusCode();
        }

        return await res.Content.ReadFromJsonAsync<BoardSyncDelta>(Serializer, cancellationToken);
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

            return s ?? throw new InvalidOperationException("Empty board response.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Could not reach the API. Start App.Web, then try again. On the Android emulator use base URL http://10.0.2.2:5031 (127.0.0.1 is the emulator itself).",
                ex);
        }
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
        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/board/{section}")
        {
            Content = JsonContent.Create(new ItemTitleRequest(title, itemId), options: Serializer)
        };
        AddMutationHeaders(req, operationId, null);
        using var res = await Client.SendAsync(req, cancellationToken);
        await ThrowIfConflictAsync(res, cancellationToken);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BoardItem>(Serializer, cancellationToken))!;
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
        using var req = new HttpRequestMessage(HttpMethod.Put, $"api/board/{section}/{itemId}")
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
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"api/board/{section}/{itemId}");
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        if (res.StatusCode == HttpStatusCode.NotFound) return false;

        await ThrowIfConflictAsync(res, cancellationToken);
        res.EnsureSuccessStatusCode();
        return true;
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
        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/board/{section}/{itemId}/toggle");
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
        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/board/dailies/{itemId}/complete-for-date")
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
        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/board/habits/{itemId}/increment");
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
        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/board/habits/{itemId}/decrement");
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        bool trackPlus,
        bool trackMinus,
        HabitResetPeriod resetPeriod,
        int counter,
        int negativeCounter,
        string? checklistJson = null,
        double? sortOrder = null,
        CancellationToken cancellationToken = default) =>
        UpdateHabitAsync(
            itemId,
            title,
            notes,
            tags,
            trackPlus,
            trackMinus,
            resetPeriod,
            counter,
            negativeCounter,
            checklistJson,
            sortOrder,
            Guid.Empty,
            null,
            cancellationToken);

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        bool trackPlus,
        bool trackMinus,
        HabitResetPeriod resetPeriod,
        int counter,
        int negativeCounter,
        string? checklistJson,
        double? sortOrder,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var body = new HabitUpdateRequest(
            title,
            notes,
            tags,
            trackPlus,
            trackMinus,
            resetPeriod,
            counter,
            negativeCounter,
            checklistJson,
            sortOrder);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"api/board/habits/{itemId}")
        {
            Content = JsonContent.Create(body, options: Serializer)
        };
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        double? sortOrder = null,
        CancellationToken cancellationToken = default) =>
        UpdateTodoAsync(
            itemId,
            title,
            notes,
            tags,
            checklistJson,
            dueDate,
            sortOrder,
            Guid.Empty,
            null,
            cancellationToken);

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        double? sortOrder,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var body = new TodoUpdateRequest(title, notes, tags, checklistJson, dueDate, sortOrder);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"api/board/todos/{itemId}")
        {
            Content = JsonContent.Create(body, options: Serializer)
        };
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        DateTime? startDate,
        DailyRepeatType repeatType,
        int repeatInterval,
        string? checklistJson,
        int streak,
        double? sortOrder = null,
        CancellationToken cancellationToken = default) =>
        UpdateDailyAsync(
            itemId,
            title,
            notes,
            tags,
            startDate,
            repeatType,
            repeatInterval,
            checklistJson,
            streak,
            sortOrder,
            Guid.Empty,
            null,
            cancellationToken);

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        DateTime? startDate,
        DailyRepeatType repeatType,
        int repeatInterval,
        string? checklistJson,
        int streak,
        double? sortOrder,
        Guid operationId,
        DateTimeOffset? expectedServerUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var body = new DailyUpdateRequest(
            title,
            notes,
            tags,
            startDate,
            repeatType,
            repeatInterval,
            checklistJson,
            streak,
            sortOrder);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"api/board/dailies/{itemId}")
        {
            Content = JsonContent.Create(body, options: Serializer)
        };
        AddMutationHeaders(req, operationId, expectedServerUpdatedAtUtc);
        using var res = await Client.SendAsync(req, cancellationToken);
        return await ReadBoardItemOrNullAsync(res, cancellationToken);
    }

    private static async Task<BoardItem?> ReadBoardItemOrNullAsync(HttpResponseMessage res,
        CancellationToken cancellationToken)
    {
        if (res.StatusCode == HttpStatusCode.NotFound) return null;

        await ThrowIfConflictAsync(res, cancellationToken);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<BoardItem>(Serializer, cancellationToken);
    }
}
