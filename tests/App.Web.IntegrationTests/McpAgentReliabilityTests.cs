using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace App.Web.IntegrationTests;

/// <summary>
/// Reliability tests for the MCP board tools: actionable errors, flexible tags,
/// per-item batch results, idempotency and duplicate warnings, native checklist
/// arrays, headless confirmation guidance, device flow, dry-run validation,
/// schema-docs conformance, due digest, and tag parity.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public sealed partial class McpAgentReliabilityTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions TestJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record BatchCreateItemResult(
        int Index,
        bool Ok,
        Guid? Id,
        BoardItem? Item,
        string? Field,
        string? Error,
        string? Message,
        bool DryRun);

    private sealed record DueDigestEntry(Guid Id, string Title, DateOnly? DueDate, BoardSection Section);

    private sealed record DueDigest(
        DueDigestEntry[] Overdue,
        DueDigestEntry[] DueToday,
        DueDigestEntry[] DueTomorrow,
        int Total);

    private static readonly string[] WorkUrgentTags = ["work", "urgent"];
    private static readonly string[] HomeTags = ["home"];

    [Fact]
    public async Task Mcp_Tags_AcceptArrayAndString()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var fromArray = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Grocery run {stamp}",
                ["tags"] = WorkUrgentTags,
            }), json)!;
        fromArray.Tags.Should().Be("work,urgent");

        var fromString = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Garage cleanup {stamp}",
                ["tags"] = "work, urgent",
            }), json)!;
        fromString.Tags.Should().Be("work,urgent");

        var updated = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_todo", new Dictionary<string, object?>
            {
                ["itemId"] = fromString.Id,
                ["tags"] = HomeTags,
            }), json)!;
        updated.Tags.Should().Be("home");

        var byTag = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["tag"] = "work" }), json)!;
        byTag.Select(t => t.Id).Should().Contain(fromArray.Id);

        var tags = JsonSerializer.Deserialize<string[]>(
            await CallTextAsync(mcp, "list_tags", []), json)!;
        tags.Should().Contain("work");
        tags.Should().Contain("urgent");
    }

    [Fact]
    public async Task Mcp_Errors_NameFieldAndExpectedFormat()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var tagsEx = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Invalid tags case {stamp}",
                ["tags"] = 42,
            }));
        tagsEx.Message.Should().Contain("tags");
        tagsEx.Message.Should().Contain("comma-separated");

        var dateEx = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Invalid date case {stamp}",
                ["date"] = "not-a-date",
            }));
        dateEx.Message.Should().Contain("date");
        dateEx.Message.Should().Contain("yyyy-MM-dd");
    }

    [Fact]
    public async Task Mcp_BatchCreate_ReturnsPerItemResults()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var results = JsonSerializer.Deserialize<BatchCreateItemResult[]>(
            await CallTextAsync(mcp, "batch_create_items", new Dictionary<string, object?>
            {
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"Valid todo item {stamp}" },
                    new Dictionary<string, object?> { ["section"] = "Habit", ["title"] = $"Invalid habit {stamp}", ["date"] = "2026-01-05" },
                    new Dictionary<string, object?> { ["section"] = "Daily", ["title"] = $"Valid daily {stamp}" },
                }
            }), json)!;

        results.Should().HaveCount(3);
        results[0].Ok.Should().BeTrue();
        results[0].Id.Should().NotBeNull();
        results[1].Ok.Should().BeFalse();
        results[1].Field.Should().Be("date");
        results[1].Message.Should().NotBeNullOrWhiteSpace();
        results[2].Ok.Should().BeTrue();

        var found = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["query"] = stamp }), json)!;
        found.Select(i => i.Title).Should().Contain($"Valid todo item {stamp}");
        found.Select(i => i.Title).Should().Contain($"Valid daily {stamp}");
        found.Select(i => i.Title).Should().NotContain($"Invalid habit {stamp}");
    }

    [Fact]
    public async Task Mcp_BatchCreate_SameKeyTwice_CreatesOnce()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var key = $"batch-{stamp}";

        Dictionary<string, object?> BatchArgs() => new()
        {
            ["items"] = new object[]
            {
                new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"First batch item {stamp}" },
                new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"Second batch item {stamp}" },
            },
            ["idempotencyKey"] = key,
        };

        var first = JsonSerializer.Deserialize<BatchCreateItemResult[]>(
            await CallTextAsync(mcp, "batch_create_items", BatchArgs()), json)!;
        first.All(r => r.Ok).Should().BeTrue();

        var second = JsonSerializer.Deserialize<BatchCreateItemResult[]>(
            await CallTextAsync(mcp, "batch_create_items", BatchArgs()), json)!;
        second.Select(r => r.Id).Should().BeEquivalentTo(first.Select(r => r.Id));

        var found = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["query"] = stamp }), json)!;
        found.Should().HaveCount(2);
    }

    [Fact]
    public async Task Mcp_CreateItem_SameIdempotencyKeyTwice_ReturnsOriginal()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var key = $"item-{stamp}";

        Dictionary<string, object?> CreateArgs() => new()
        {
            ["section"] = "Todo",
            ["title"] = $"One-off task {stamp}",
            ["idempotencyKey"] = key,
        };

        var first = JsonSerializer.Deserialize<BoardItem>(await CallTextAsync(mcp, "create_item", CreateArgs()), json)!;
        var second = JsonSerializer.Deserialize<BoardItem>(await CallTextAsync(mcp, "create_item", CreateArgs()), json)!;
        second.Id.Should().Be(first.Id);

        // Same key with different fields must fail loudly instead of duplicating.
        var mismatch = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Other task {stamp}",
                ["idempotencyKey"] = key,
            }));
        mismatch.Message.Should().Contain("idempotencyKey");
    }

    [Fact]
    public async Task Mcp_CreateItem_DuplicateTitle_WarnsWithExisting()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;

        var first = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = "Resume and portfolio polish",
            }), json)!;

        var dupe = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = "Resume and portfolio polish",
            }));
        dupe.Message.Should().Contain("Similar item exists");
        dupe.Message.Should().Contain("Resume and portfolio polish");
        dupe.Message.Should().Contain(first.Id.ToString("D"));

        var forced = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = "Resume and portfolio polish",
                ["allowDuplicate"] = true,
            }), json)!;
        forced.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public async Task Mcp_SetChecklist_AcceptsNativeArrayAndString()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Checklist task {stamp}",
            }), json)!;

        var fromArray = JsonSerializer.Deserialize<DailyChecklistItem[]>(
            await CallTextAsync(mcp, "set_checklist", new Dictionary<string, object?>
            {
                ["itemId"] = created.Id,
                ["json"] = new object[]
                {
                    new Dictionary<string, object?> { ["text"] = "First step", ["isDone"] = false },
                    new Dictionary<string, object?> { ["text"] = "Second step", ["isDone"] = true },
                },
            }), json)!;
        fromArray.Should().HaveCount(2);
        fromArray.Select(i => i.Text).Should().Contain(["First step", "Second step"]);
        fromArray.First(i => i.Text == "Second step").IsDone.Should().BeTrue();

        var fromString = JsonSerializer.Deserialize<DailyChecklistItem[]>(
            await CallTextAsync(mcp, "set_checklist", new Dictionary<string, object?>
            {
                ["itemId"] = created.Id,
                ["json"] = "[{\"text\":\"Third step\",\"isDone\":false}]",
            }), json)!;
        fromString.Should().HaveCount(1);
        fromString[0].Text.Should().Be("Third step");
    }

    [Fact]
    public async Task Mcp_DeleteItem_WithoutConfirm_StatesRetry()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Task to delete {stamp}",
            }), json)!;

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await mcp.CallToolAsync("delete_item", new Dictionary<string, object?> { ["itemId"] = created.Id }));
        (ex.Message.Contains("confirm", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("elicit", StringComparison.OrdinalIgnoreCase)).Should().BeTrue(
            "the error must state the confirmToken retry or ask for elicitation");
    }

    [Fact]
    public async Task Mcp_BatchCreate_DryRun_ValidatesWithoutPersisting()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var before = await SnapshotCountAsync(mcp);

        var results = JsonSerializer.Deserialize<BatchCreateItemResult[]>(
            await CallTextAsync(mcp, "batch_create_items", new Dictionary<string, object?>
            {
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"Planned todo {stamp}" },
                    new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = "   " },
                },
                ["dryRun"] = true,
            }), json)!;

        results.Should().HaveCount(2);
        results[0].Ok.Should().BeTrue();
        results[0].DryRun.Should().BeTrue();
        results[0].Id.Should().NotBeNull();
        results[1].Ok.Should().BeFalse();
        results[1].Field.Should().Be("title");

        (await SnapshotCountAsync(mcp)).Should().Be(before);

        var preview = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Planned habit {stamp}",
                ["dryRun"] = true,
            }), json)!;
        preview.Title.Should().Be($"Planned habit {stamp}");
        (await SnapshotCountAsync(mcp)).Should().Be(before);
    }

    [Fact]
    public async Task Mcp_DueDigest_ListsEachTodoOnce()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        async Task<BoardItem> CreateTodo(string title, string? date) =>
            JsonSerializer.Deserialize<BoardItem>(
                await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
                {
                    ["section"] = "Todo",
                    ["title"] = $"{title} {stamp}",
                    ["date"] = date!,
                }), json)!;

        var overdue = await CreateTodo("Overdue application", today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var dueToday = await CreateTodo("Filing due today", "today");
        var dueTomorrow = await CreateTodo("Interview tomorrow", "tomorrow");
        var later = await CreateTodo("Later plan", today.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        _ = later;

        var digest = JsonSerializer.Deserialize<DueDigest>(
            await CallTextAsync(mcp, "get_due_digest", []), json)!;
        digest.Overdue.Select(e => e.Id).Should().Contain(overdue.Id);
        digest.DueToday.Select(e => e.Id).Should().Contain(dueToday.Id);
        digest.DueTomorrow.Select(e => e.Id).Should().Contain(dueTomorrow.Id);

        // New accounts also hold seeded items: scope the exactness check to this test.
        var ours = digest.Overdue.Concat(digest.DueToday)
            .Concat(digest.DueTomorrow)
            .Where(e => e.Title.Contains(stamp, StringComparison.Ordinal))
            .Select(e => e.Id)
            .ToList();
        ours.Should().HaveCount(3);
        ours.Should().OnlyHaveUniqueItems("a todo due tomorrow produces exactly one digest entry");
    }

    [Fact]
    public async Task Mcp_DeviceFlow_MintsWorkingTokenWithoutPastedSecret()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"device-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password)))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var jwt = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        // 1. The headless agent starts a device authorization and shows the code.
        var start = await client.PostAsJsonAsync("/oauth/device/authorize", new { client_id = "opencode", scope = "full" });
        start.EnsureSuccessStatusCode();
        var startDoc = await start.Content.ReadFromJsonAsync<JsonElement>();
        var deviceCode = startDoc.GetProperty("device_code").GetString()!;
        var userCode = startDoc.GetProperty("user_code").GetString()!;
        startDoc.GetProperty("verification_uri").GetString().Should().EndWith("/oauth/device");
        userCode.Should().MatchRegex("^[A-Z2-9]{4}-[A-Z2-9]{4}$");

        // 2. Polling before approval reports pending, not a token.
        var pending = await client.PostAsJsonAsync("/oauth/token", new
        {
            grant_type = "urn:ietf:params:oauth:grant-type:device_code",
            device_code = deviceCode,
            client_id = "opencode",
        });
        pending.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pending.Content.ReadAsStringAsync()).Should().Contain("authorization_pending");

        // 3. The user approves once in the browser (authenticated, no pasted secret).
        using var approve = new HttpRequestMessage(HttpMethod.Post, "/oauth/device/approve")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["user_code"] = userCode,
                ["action"] = "approve",
            }),
        };
        approve.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var approved = await client.SendAsync(approve);
        approved.EnsureSuccessStatusCode();
        (await approved.Content.ReadAsStringAsync()).Should().Contain("Approved");

        // 4. The agent polls again and receives working tokens.
        var token = await client.PostAsJsonAsync("/oauth/token", new
        {
            grant_type = "urn:ietf:params:oauth:grant-type:device_code",
            device_code = deviceCode,
            client_id = "opencode",
        });
        token.EnsureSuccessStatusCode();
        var tokenDoc = await token.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokenDoc.GetProperty("accessToken").GetString()!;
        accessToken.Should().NotBeNullOrWhiteSpace();

        using var mcpCall = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new { },
            }),
        };
        mcpCall.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        mcpCall.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        mcpCall.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        (await client.SendAsync(mcpCall)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Mcp_Schema_MatchesDocs()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var docs = await File.ReadAllTextAsync(FindDocsMcpMd());
        var markers = McpSchemaMarkerRegex().Matches(docs);
        markers.Should().NotBeEmpty("docs/mcp.md must carry machine-readable tool schemas");

        var marked = new Dictionary<string, (HashSet<string> Required, HashSet<string> Optional)>(StringComparer.Ordinal);
        foreach (Match marker in markers)
        {
            var name = marker.Groups["name"].Value;
            marked[name] = (
                SplitParams(marker.Groups["required"].Value),
                SplitParams(marker.Groups["optional"].Value));
        }

        var tools = await mcp.ListToolsAsync();
        tools.Should().NotBeEmpty();
        var liveNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            liveNames.Add(tool.Name);
            docs.Should().Contain(tool.Name, $"docs/mcp.md must mention tool '{tool.Name}'");
            marked.Should().ContainKey(tool.Name, $"docs/mcp.md must carry a schema marker for tool '{tool.Name}'");

            var (properties, required) = ReadInputSchema(tool);
            var (markerRequired, markerOptional) = marked[tool.Name];
            required.Should().BeEquivalentTo(markerRequired, $"required params of '{tool.Name}' must match docs/mcp.md");
            properties.Should().BeEquivalentTo(
                markerRequired.Concat(markerOptional),
                $"all params of '{tool.Name}' must match docs/mcp.md");
        }

        foreach (var name in marked.Keys)
        {
            liveNames.Should().Contain(name, $"marker for removed tool '{name}' must be deleted from docs/mcp.md");
        }
    }

    private static HashSet<string> SplitParams(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    private static (HashSet<string> Properties, HashSet<string> Required) ReadInputSchema(McpClientTool tool)
    {
        var element = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
        var properties = new HashSet<string>(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in props.EnumerateObject())
            {
                properties.Add(property.Name);
            }
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("required", out var req)
            && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in req.EnumerateArray())
            {
                if (item.GetString() is { } name)
                {
                    required.Add(name);
                }
            }
        }

        return (properties, required);
    }

    private static string FindDocsMcpMd()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "mcp.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("docs/mcp.md not found above test binaries.");
    }

    [GeneratedRegex("<!-- mcp-tool-schema:\\s*(?<name>[a-z_0-9]+)\\s+required\\((?<required>[^)]*)\\)\\s+optional\\((?<optional>[^)]*)\\)\\s*-->")]
    private static partial Regex McpSchemaMarkerRegex();

    private async Task<McpClient> CreateLoggedInMcpClientAsync()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"reliable-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(client.BaseAddress!, "/mcp"), },
            client,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<int> SnapshotCountAsync(McpClient mcp)
    {
        var snapshot = JsonSerializer.Deserialize<BoardSnapshot>(await CallTextAsync(mcp, "get_snapshot", []), TestJson)!;
        return snapshot.Habits.Count + snapshot.Dailies.Count + snapshot.Todos.Count;
    }

    private static async Task<string> CallTextAsync(
        McpClient mcp, string name, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(name, args);
        result.IsError.Should().Be(null);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
    }
}
