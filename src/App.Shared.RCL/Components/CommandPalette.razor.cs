using App.Shared.RCL.Models;
using App.Shared.RCL.Services.CommandPalette;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class CommandPalette : IDisposable
{
    private readonly List<string> _breadcrumbs = [];
    private readonly Stack<(string Title, List<CommandItem> Items)> _breadcrumbStack = [];
    private List<CommandItem> _rootItems = [];
    private List<CommandItem>? _currentSubItems;
    private List<CommandItem> _flatVisibleItems = [];
    private List<IGrouping<string, CommandItem>> _groupedItems = [];
    private readonly Dictionary<string, int> _groupTotals =
#pragma warning disable IDE0028 // Dictionary with comparer cannot use collection expression.
        new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    private string _query = string.Empty;
    private int _selectedIndex;
    private CancellationTokenSource? _searchCts;
    private bool _pendingChord;
    private DotNetObjectReference<CommandPalette>? _dotNetRef;
    private bool _globalShortcutRegistered;

    protected override void OnInitialized()
    {
        PaletteService.StateChanged += HandleServiceStateChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_globalShortcutRegistered)
        {
            _globalShortcutRegistered = true;
            _dotNetRef ??= DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("HabitinatorCommandPalette.initGlobal", _dotNetRef);
            }
            catch
            {
                // Best effort global shortcut.
            }
        }
    }

    [JSInvokable("ToggleFromJs")]
    public void ToggleFromJs()
    {
        PaletteService.Toggle();
    }

    private void HandleServiceStateChanged()
    {
        _ = InvokeAsync(async () =>
        {
            if (PaletteService.IsOpen)
            {
                _breadcrumbs.Clear();
                _breadcrumbStack.Clear();
                _currentSubItems = null;
                _query = string.Empty;
                _selectedIndex = 0;
                _pendingChord = false;
                _rootItems = await PaletteService.GetRootCommandsAsync();
                await LoadRecentAsync();
                await RecomputeVisibleItemsAsync();
                StateHasChanged();
                await SafeInvokeVoidAsync("HabitinatorCommandPalette.onOpen");
            }
            else
            {
                if (_searchCts is not null)
                {
                    await _searchCts.CancelAsync();
                    _searchCts.Dispose();
                    _searchCts = null;
                }

                StateHasChanged();
                await SafeInvokeVoidAsync("HabitinatorCommandPalette.onClose");
            }
        });
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            var ids = await JS.InvokeAsync<string[]?>("HabitinatorCommandPalette.loadRecent", CancellationToken.None, null);
            if (ids is not null)
            {
                PaletteService.SetRecentIds(ids);
            }
        }
        catch
        {
            // Local storage unavailable.
        }
    }

    private async Task SaveRecentAsync(string commandId)
    {
        try
        {
            PaletteService.RecordRecent(commandId);
            await JS.InvokeVoidAsync("HabitinatorCommandPalette.saveRecent", CancellationToken.None, new object?[] { PaletteService.RecentIds.ToArray() });
        }
        catch
        {
            PaletteService.RecordRecent(commandId);
        }
    }

    private async Task OnQueryInput(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? string.Empty;
        _selectedIndex = 0;
        await RecomputeVisibleItemsAsync();
    }

    private async Task RecomputeVisibleItemsAsync()
    {
        var token = await ResetSearchAsync();
        // Debounce fast typing so snapshot reads do not stack up.
        if (!string.IsNullOrWhiteSpace(_query) && _currentSubItems is null)
        {
            try
            {
                await Task.Delay(120, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var items = _currentSubItems is not null
            ? FilterSubItems(_query)
            : await BuildRootItemsAsync(_query, token);
        if (items is null || token.IsCancellationRequested)
        {
            return;
        }

        ApplyGrouping(items);

        if (_selectedIndex >= _flatVisibleItems.Count)
        {
            _selectedIndex = Math.Max(0, _flatVisibleItems.Count - 1);
        }

        StateHasChanged();
    }

    private async Task<CancellationToken> ResetSearchAsync()
    {
        if (_searchCts is not null)
        {
            await _searchCts.CancelAsync();
            _searchCts.Dispose();
        }

        _searchCts = new CancellationTokenSource();
        return _searchCts.Token;
    }

    private List<CommandItem> FilterSubItems(string query)
    {
        if (_currentSubItems is null)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return [.. _currentSubItems];
        }

        return _currentSubItems
            .Select(i => (Item: i, Score: CommandPaletteScoring.ScoreItem(i, query.Trim())))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Item)
            .ToList();
    }

    private async Task<List<CommandItem>?> BuildRootItemsAsync(string query, CancellationToken token)
    {
        var effective = query.Trim();
        var commandOnly = effective.StartsWith('>');
        if (commandOnly)
        {
            effective = effective[1..].Trim();
        }

        var isAiMode = effective.StartsWith('?') || effective.StartsWith('/');
        if (isAiMode)
        {
            effective = effective[1..].Trim();
        }

        var isTagMode = effective.StartsWith('#');
        if (isTagMode)
        {
            effective = effective[1..].Trim();
        }

        var isUrgentMode = effective.StartsWith('!');
        if (isUrgentMode)
        {
            effective = effective[1..].Trim();
        }

        var isSectionMode = effective.StartsWith('@');
        string? targetSection = null;
        if (isSectionMode)
        {
            var parts = effective[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                targetSection = parts[0].ToLowerInvariant();
                effective = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(effective) && !isUrgentMode && !isTagMode && !isSectionMode && !isAiMode)
        {
            return BuildBrowseItems();
        }

        var q = effective;
        var items = new List<CommandItem>();

        // AI Mode
        if (isAiMode)
        {
            if (PaletteService.BuildAskAiCommand(string.IsNullOrWhiteSpace(q) ? "Help me plan today" : q) is { } askAiCmd)
            {
                items.Add(askAiCmd);
            }
            var quickAiPrompts = new[]
            {
                ("Plan my day", "Plan my day based on today dailies and active todos"),
                ("What is pending?", "What dailies and todos do I have left for today?"),
                ("Suggest habits", "Suggest 3 positive daily habits for better focus and health"),
                ("Focus stats", "Show my timer summary and focus minutes")
            };
            foreach (var (title, prompt) in quickAiPrompts)
            {
                if (string.IsNullOrWhiteSpace(q) || title.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new CommandItem(
                        Id: $"quick-ai-{title.GetHashCode(StringComparison.Ordinal):x}",
                        Title: title,
                        Subtitle: prompt,
                        Category: "AI assistant",
                        Icon: Icons.Material.Filled.SmartToy,
                        Action: () =>
                        {
                            PaletteService.Close();
                            PaletteService.BuildAskAiCommand(prompt)?.Action?.Invoke();
                            return Task.CompletedTask;
                        }));
                }
            }
            return items;
        }

        // 1. Matching root commands with fuzzy ranking.
        if (!isTagMode && !isUrgentMode && !isSectionMode)
        {
            items.AddRange(FindMatchingRootCommands(q));

            // 2. Quick-add: "add <section> <title> #tag".
            items.AddRange(PaletteService.BuildQuickAddCommands(q));

            // 3. Ask AI handoff.
            if (PaletteService.BuildAskAiCommand(q) is { } askAi)
            {
                items.Add(askAi);
            }
        }

        if (commandOnly)
        {
            return items
                .Select(i => (Item: i, Score: CommandPaletteScoring.ScoreItem(i, q)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)
                .ToList();
        }

        // 4. Search board items.
        var boardResults = await SearchBoardItemsSafeAsync(isTagMode ? $"#{q}" : q, token);
        if (boardResults is null)
        {
            return null;
        }

        if (!token.IsCancellationRequested)
        {
            if (isTagMode)
            {
                items.AddRange(boardResults.Where(b => b.Subtitle?.Contains('#', StringComparison.Ordinal) == true || b.Title.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }
            else if (isUrgentMode)
            {
                items.AddRange(boardResults.Where(b => b.Subtitle?.Contains("Due", StringComparison.OrdinalIgnoreCase) == true ||
                                                       b.Subtitle?.Contains("Daily", StringComparison.OrdinalIgnoreCase) == true && !b.Subtitle.Contains("completed today", StringComparison.OrdinalIgnoreCase) ||
                                                       b.Title.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }
            else if (isSectionMode && targetSection is not null)
            {
                items.AddRange(boardResults.Where(b =>
                    (targetSection.StartsWith("habit", StringComparison.Ordinal) && b.Category == "Habits") ||
                    (targetSection.StartsWith("dail", StringComparison.Ordinal) && b.Category == "Dailies") ||
                    (targetSection.StartsWith("todo", StringComparison.Ordinal) && b.Category == "To-dos")));
            }
            else
            {
                items.AddRange(boardResults);
            }
        }

        return items;
    }

    private List<CommandItem> BuildBrowseItems()
    {
        // Show recent first, then every category capped with totals in the group header.
        var recent = PaletteService.BuildRecentGroup(_rootItems);
        var ordered = new List<CommandItem>();
        ordered.AddRange(recent);
        ordered.AddRange(_rootItems);
        return ordered;
    }

    private List<CommandItem> FindMatchingRootCommands(string q)
    {
        return _rootItems
            .Select(i => (Item: i, Score: CommandPaletteScoring.ScoreItem(i, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Item)
            .ToList();
    }

    private async Task<List<CommandItem>?> SearchBoardItemsSafeAsync(string query, CancellationToken token)
    {
        try
        {
            return await PaletteService.SearchBoardItemsAsync(query, token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private void ApplyGrouping(List<CommandItem> items)
    {
        _groupTotals.Clear();
        foreach (var g in items.GroupBy(i => i.Category))
        {
            _groupTotals[g.Key] = g.Count();
        }

        var maxItemsPerCategory = GetMaxItemsPerCategory();

        // GroupBy preserves first-seen order so Recent stays on top.
        _groupedItems = items
            .GroupBy(i => i.Category)
            .Select(g => new CappedGrouping(g.Key, g.Take(maxItemsPerCategory)))
            .Cast<IGrouping<string, CommandItem>>()
            .ToList();

        _flatVisibleItems = _groupedItems.SelectMany(g => g).ToList();
    }

    private int GetMaxItemsPerCategory()
    {
        if (_currentSubItems is not null)
        {
            return 100;
        }

        return string.IsNullOrWhiteSpace(_query) ? 6 : 10;
    }

    public string GetGroupTitle(string category)
    {
        if (_groupTotals.TryGetValue(category, out var total))
        {
            var shown = _groupedItems.FirstOrDefault(g => g.Key == category)?.Count() ?? 0;
            if (total > shown)
            {
                return $"{category} ({shown} of {total})";
            }

            return $"{category} ({total})";
        }

        return category;
    }

    public CommandItem? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _flatVisibleItems.Count
            ? _flatVisibleItems[_selectedIndex]
            : null;

    public string? SelectedItemId => SelectedItem?.Id;

    private void SelectItem(CommandItem item)
    {
        var idx = _flatVisibleItems.FindIndex(i => i.Id == item.Id);
        if (idx >= 0 && idx != _selectedIndex)
        {
            _selectedIndex = idx;
        }
    }

    private static string[] GetShortcutParts(string badge)
    {
        if (badge.Contains(' '))
        {
            return badge.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        if (badge.Contains('+'))
        {
            return badge.Split('+', StringSplitOptions.RemoveEmptyEntries);
        }

        return [badge];
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (await HandleChordAsync(e))
        {
            return;
        }

        if (await HandleCreationShortcutAsync(e))
        {
            return;
        }

        switch (e.Key)
        {
            case "ArrowDown":
                await MoveSelectionAsync(1);
                break;

            case "ArrowUp":
                await MoveSelectionAsync(-1);
                break;

            case "Home":
                await MoveToEdgeAsync(first: true);
                break;

            case "End":
                await MoveToEdgeAsync(first: false);
                break;

            case "PageDown":
                await MoveSelectionAsync(5);
                break;

            case "PageUp":
                await MoveSelectionAsync(-5);
                break;

            case "Enter":
                await ExecuteSelectedAsync();
                break;

            case "ArrowRight" when SelectedItem?.ChildrenProvider is not null:
                await DrillDownAsync(SelectedItem);
                break;

            case "ArrowLeft" when _breadcrumbStack.Count > 0:
                await PopBreadcrumbAsync();
                break;

            case "Backspace" when string.IsNullOrEmpty(_query) && _breadcrumbStack.Count > 0:
                await PopBreadcrumbAsync();
                break;

            case "Escape":
                await HandleEscapeAsync();
                break;
        }
    }

    private async Task<bool> HandleChordAsync(KeyboardEventArgs e)
    {
        // When the query is empty, press G then B, S, U, or P.
        if (_currentSubItems is not null)
        {
            _pendingChord = false;
            return false;
        }

        if (_pendingChord)
        {
            _pendingChord = false;
            var path = e.Key.ToLowerInvariant() switch
            {
                "b" => "/",
                "s" => "/stats",
                "u" => "/upcoming",
                "p" => "/settings",
                _ => null
            };
            if (path is not null && string.IsNullOrWhiteSpace(_query))
            {
                var nav = _rootItems.FirstOrDefault(c => c.Id == $"nav-{PathToNavId(path)}");
                if (nav?.Action is not null)
                {
                    await SaveRecentAsync(nav.Id);
                    await nav.Action();
                    return true;
                }
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(_query) && e.Key.Equals("g", StringComparison.OrdinalIgnoreCase)
            && !e.CtrlKey && !e.MetaKey && !e.AltKey)
        {
            _pendingChord = true;
            StateHasChanged();
            return true;
        }

        return false;
    }

    private static string PathToNavId(string path) => path switch
    {
        "/" => "board",
        "/stats" => "stats",
        "/upcoming" => "upcoming",
        "/settings" => "settings",
        _ => "board"
    };

    private async Task<bool> HandleCreationShortcutAsync(KeyboardEventArgs e)
    {
        BoardSection? section = null;
        if (e.CtrlKey && e.Key.Equals("h", StringComparison.OrdinalIgnoreCase))
        {
            section = BoardSection.Habit;
        }
        else if (e.CtrlKey && e.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
        {
            section = BoardSection.Daily;
        }
        else if (e.AltKey && e.Key.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            section = BoardSection.Todo;
        }

        if (section is null)
        {
            return false;
        }

        await PaletteService.CreateItemAsync(section.Value);
        return true;
    }

    private async Task MoveSelectionAsync(int delta)
    {
        if (_flatVisibleItems.Count == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + delta + _flatVisibleItems.Count) % _flatVisibleItems.Count;
        StateHasChanged();
        await SafeInvokeVoidAsync("HabitinatorCommandPalette.scrollSelectedIntoView", SelectedItemId);
    }

    private async Task MoveToEdgeAsync(bool first)
    {
        if (_flatVisibleItems.Count == 0)
        {
            return;
        }

        _selectedIndex = first ? 0 : _flatVisibleItems.Count - 1;
        StateHasChanged();
        await SafeInvokeVoidAsync("HabitinatorCommandPalette.scrollSelectedIntoView", SelectedItemId);
    }

    private Task ExecuteSelectedAsync()
    {
        return SelectedItem is not null ? ExecuteItemAsync(SelectedItem) : Task.CompletedTask;
    }

    private async Task HandleEscapeAsync()
    {
        if (_pendingChord)
        {
            _pendingChord = false;
            StateHasChanged();
            return;
        }

        if (!string.IsNullOrEmpty(_query))
        {
            _query = string.Empty;
            _selectedIndex = 0;
            await RecomputeVisibleItemsAsync();
        }
        else if (_breadcrumbStack.Count > 0)
        {
            await PopBreadcrumbAsync();
        }
        else
        {
            PaletteService.Close();
        }
    }

    private async Task ExecuteItemAsync(CommandItem item)
    {
        if (item.PrimaryAction is not null)
        {
            await SaveRecentAsync(item.Id);
            try
            {
                await item.PrimaryAction();
            }
            catch
            {
                // Action hosts already notify. Keep palette stable.
            }
        }
        else if (item.ChildrenProvider is not null)
        {
            await DrillDownAsync(item);
        }
        else if (item.Action is not null)
        {
            await SaveRecentAsync(item.Id);
            try
            {
                await item.Action();
            }
            catch
            {
                // Action hosts already notify. Keep palette stable.
            }
        }
    }

    private async Task DrillDownAsync(CommandItem item)
    {
        if (item.ChildrenProvider is null)
        {
            return;
        }

        try
        {
            var children = await item.ChildrenProvider();
            _breadcrumbStack.Push((item.Title, _currentSubItems ?? _rootItems));
            _breadcrumbs.Add(item.Title);
            _currentSubItems = children;
            _query = string.Empty;
            _selectedIndex = 0;
            _pendingChord = false;
            await RecomputeVisibleItemsAsync();
        }
        catch
        {
            // Ignore sub-item loading errors
        }
    }

    private async Task PopBreadcrumbAsync()
    {
        if (_breadcrumbStack.Count == 0)
        {
            return;
        }

        var (_, previousItems) = _breadcrumbStack.Pop();
        if (_breadcrumbs.Count > 0)
        {
            _breadcrumbs.RemoveAt(_breadcrumbs.Count - 1);
        }

        _currentSubItems = _breadcrumbStack.Count > 0 ? previousItems : null;
        _query = string.Empty;
        _selectedIndex = 0;
        await RecomputeVisibleItemsAsync();
    }

    private string GetSearchPlaceholder()
    {
        if (_breadcrumbs.Count > 0)
        {
            return $"Search actions for {_breadcrumbs[^1]}...";
        }

        if (_pendingChord)
        {
            return "G pressed. Press B, S, U, or P...";
        }

        if (_query.StartsWith('>'))
        {
            return "Commands mode (e.g. navigation, export, appearance)...";
        }

        if (_query.StartsWith('?') || _query.StartsWith('/'))
        {
            return "Ask AI assistant anything...";
        }

        if (_query.StartsWith('#'))
        {
            return "Filtering by tag (e.g. #work, #health)...";
        }

        if (_query.StartsWith('!'))
        {
            return "Showing urgent and overdue items...";
        }

        if (_query.StartsWith('@'))
        {
            return "Filter by section (@habits, @dailies, @todos)...";
        }

        return "Type a command or search. Try >, ?, #tag, !urgent, add todo ...";
    }

    public string GetSearchResultsStatus()
    {
        if (_flatVisibleItems.Count == 0)
        {
            return "No matching commands or items";
        }

        return $"{_flatVisibleItems.Count} commands and items found";
    }

    private string? GetActiveDescendantId()
    {
        return SelectedItemId is not null ? $"cmd-item-{SelectedItemId}" : null;
    }

    private static string GetHighlightedHtml(string text, string query)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(text);
        var q = query.Trim().TrimStart('>');
        if (string.IsNullOrWhiteSpace(q) || q.StartsWith("add ", StringComparison.OrdinalIgnoreCase))
        {
            return encoded;
        }

        var idx = encoded.IndexOf(System.Net.WebUtility.HtmlEncode(q), StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // Fall back to first-word highlight for fuzzy matches.
            var first = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(first))
            {
                return encoded;
            }

            idx = encoded.IndexOf(System.Net.WebUtility.HtmlEncode(first), StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return encoded;
            }

            q = first;
        }

        var encodedQ = System.Net.WebUtility.HtmlEncode(q);
        return string.Concat(
            encoded.AsSpan(0, idx).ToString(),
            "<mark class=\"cmd-palette-mark\">",
            encoded[idx..(idx + encodedQ.Length)],
            "</mark>",
            encoded[(idx + encodedQ.Length)..]);
    }

    private async ValueTask SafeInvokeVoidAsync(string identifier, params object?[]? args)
    {
        try
        {
            await JS.InvokeVoidAsync(identifier, CancellationToken.None, args);
        }
        catch
        {
            // Ignored when JS is not ready or during prerendering
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            PaletteService.StateChanged -= HandleServiceStateChanged;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
            _dotNetRef?.Dispose();
            _dotNetRef = null;
        }
    }

    private sealed class CappedGrouping(string key, IEnumerable<CommandItem> items) : IGrouping<string, CommandItem>
    {
        private readonly List<CommandItem> _items = items.ToList();

        public string Key { get; } = key;

        public IEnumerator<CommandItem> GetEnumerator() => _items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
