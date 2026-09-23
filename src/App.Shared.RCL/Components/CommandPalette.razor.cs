using App.Shared.RCL.Models;
using App.Shared.RCL.Services.CommandPalette;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace App.Shared.RCL.Components;

public partial class CommandPalette : IDisposable
{
    private readonly List<string> _breadcrumbs = [];
    private readonly Stack<(string Title, List<CommandItem> Items)> _breadcrumbStack = [];
    private List<CommandItem> _rootItems = [];
    private List<CommandItem>? _currentSubItems;
    private List<CommandItem> _flatVisibleItems = [];
    private List<IGrouping<string, CommandItem>> _groupedItems = [];
    private string _query = string.Empty;
    private int _selectedIndex;
    private CancellationTokenSource? _searchCts;

    protected override void OnInitialized()
    {
        PaletteService.StateChanged += HandleServiceStateChanged;
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
                _rootItems = await PaletteService.GetRootCommandsAsync();
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

    private async Task OnQueryInput(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? string.Empty;
        _selectedIndex = 0;
        await RecomputeVisibleItemsAsync();
    }

    private async Task RecomputeVisibleItemsAsync()
    {
        if (_searchCts is not null)
        {
            await _searchCts.CancelAsync();
            _searchCts.Dispose();
        }

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        var items = new List<CommandItem>();

        if (_currentSubItems is not null)
        {
            // Drilled down into sub-actions
            if (string.IsNullOrWhiteSpace(_query))
            {
                items.AddRange(_currentSubItems);
            }
            else
            {
                items.AddRange(_currentSubItems.Where(i =>
                    i.Title.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
                    (i.Subtitle is not null && i.Subtitle.Contains(_query, StringComparison.OrdinalIgnoreCase)) ||
                    (i.Keywords is not null && i.Keywords.Any(k => k.Contains(_query, StringComparison.OrdinalIgnoreCase)))));
            }
        }
        else
        {
            // Root level: progressive disclosure
            if (string.IsNullOrWhiteSpace(_query))
            {
                // Initial state: show Suggested, Navigation, and top Actions
                items.AddRange(_rootItems.Where(i => i.Category is "Suggested" or "Navigation" or "Actions"));
            }
            else
            {
                var q = _query.Trim();

                // 1. Matching root commands
                var matchingRoot = _rootItems.Where(i =>
                    i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    i.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (i.Subtitle is not null && i.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (i.Keywords is not null && i.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase))))
                    .ToList();

                items.AddRange(matchingRoot);

                // 2. Search board items
                try
                {
                    var boardResults = await PaletteService.SearchBoardItemsAsync(q, token);
                    if (!token.IsCancellationRequested)
                    {
                        items.AddRange(boardResults);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        // Group with capped items per category when browsing root, but never truncate sub-actions
        int maxItemsPerCategory;
        if (_currentSubItems is not null)
        {
            maxItemsPerCategory = 100;
        }
        else if (string.IsNullOrWhiteSpace(_query))
        {
            maxItemsPerCategory = 4;
        }
        else
        {
            maxItemsPerCategory = 8;
        }
        _groupedItems = items
            .GroupBy(i => i.Category)
            .Select(g => new CappedGrouping(g.Key, g.Take(maxItemsPerCategory)))
            .Cast<IGrouping<string, CommandItem>>()
            .ToList();

        _flatVisibleItems = _groupedItems.SelectMany(g => g).ToList();

        if (_selectedIndex >= _flatVisibleItems.Count)
        {
            _selectedIndex = Math.Max(0, _flatVisibleItems.Count - 1);
        }
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
        if (e.CtrlKey && e.Key.Equals("h", StringComparison.OrdinalIgnoreCase))
        {
            await PaletteService.CreateItemAsync(BoardSection.Habit);
            return;
        }

        if (e.CtrlKey && e.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
        {
            await PaletteService.CreateItemAsync(BoardSection.Daily);
            return;
        }

        if (e.AltKey && e.Key.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            await PaletteService.CreateItemAsync(BoardSection.Todo);
            return;
        }

        switch (e.Key)
        {
            case "ArrowDown":
                if (_flatVisibleItems.Count > 0)
                {
                    _selectedIndex = (_selectedIndex + 1) % _flatVisibleItems.Count;
                    StateHasChanged();
                    await SafeInvokeVoidAsync("HabitinatorCommandPalette.scrollSelectedIntoView", SelectedItemId);
                }
                break;

            case "ArrowUp":
                if (_flatVisibleItems.Count > 0)
                {
                    _selectedIndex = (_selectedIndex - 1 + _flatVisibleItems.Count) % _flatVisibleItems.Count;
                    StateHasChanged();
                    await SafeInvokeVoidAsync("HabitinatorCommandPalette.scrollSelectedIntoView", SelectedItemId);
                }
                break;

            case "Enter":
                if (SelectedItem is not null)
                {
                    await ExecuteItemAsync(SelectedItem);
                }
                break;

            case "Backspace" when string.IsNullOrEmpty(_query) && _breadcrumbStack.Count > 0:
                await PopBreadcrumbAsync();
                break;

            case "Escape":
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
                break;
        }
    }

    private async Task ExecuteItemAsync(CommandItem item)
    {
        if (item.ChildrenProvider is not null)
        {
            try
            {
                var children = await item.ChildrenProvider();
                _breadcrumbStack.Push((item.Title, _currentSubItems ?? _rootItems));
                _breadcrumbs.Add(item.Title);
                _currentSubItems = children;
                _query = string.Empty;
                _selectedIndex = 0;
                await RecomputeVisibleItemsAsync();
            }
            catch
            {
                // Ignore sub-item loading errors
            }
        }
        else if (item.Action is not null)
        {
            await item.Action();
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

        return "Type a command or search...";
    }

    private string? GetActiveDescendantId()
    {
        return SelectedItemId is not null ? $"cmd-item-{SelectedItemId}" : null;
    }

    private async ValueTask SafeInvokeVoidAsync(string identifier, params object?[]? args)
    {
        try
        {
            await JS.InvokeVoidAsync(identifier, args);
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
