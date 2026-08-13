using App.Shared.RCL;
using App.Shared.RCL.Models;

using FsCheck;
using FsCheck.Xunit;

namespace App.Shared.Tests;

public sealed class BoardItemInputFuzzTests
{
    [Property]
    public void ParseTags_NeverThrowsAndEnsuresInvariants(string? raw)
    {
        var tags = BoardTagUtil.ParseTags(raw).ToList();

        foreach (var tag in tags)
        {
            Assert.False(string.IsNullOrWhiteSpace(tag));
            Assert.Equal(tag.Trim(), tag);
            Assert.DoesNotContain(",", tag);
        }
    }

    [Property]
    public void DailyChecklistJson_Parse_NeverThrows(string? json)
    {
        var exception = Xunit.Record.Exception(() => DailyChecklistJson.Parse(json));
        Assert.Null(exception);
    }

    [Property]
    public void DailyChecklistJson_RoundtripAndSanitizes(Tuple<Guid, string, bool>[] itemsInput)
    {
        if (itemsInput == null)
        {
            return;
        }

        // Map tuples to DailyChecklistItem
        var items = itemsInput.Select(t => new DailyChecklistItem(t.Item1, t.Item2, t.Item3)).ToList();

        var json = DailyChecklistJson.Serialize(items);

        if (json == null)
        {
            // If Serialize returned null, all items must have been null, empty, or whitespace
            Assert.All(items, x => Assert.True(string.IsNullOrWhiteSpace(x.Text)));
            return;
        }

        var parsed = DailyChecklistJson.Parse(json);
        Assert.NotNull(parsed);

        // Expected count is the count of items that had non-whitespace text
        var expectedCount = items.Count(x => !string.IsNullOrWhiteSpace(x.Text));
        Assert.Equal(expectedCount, parsed.Count);

        for (var i = 0; i < parsed.Count; i++)
        {
            var parsedItem = parsed[i];

            // Text must be trimmed, non-empty, and free of Zalgo Unicode stack marks
            Assert.False(string.IsNullOrWhiteSpace(parsedItem.Text));
            Assert.Equal(parsedItem.Text.Trim(), parsedItem.Text);
            Assert.False(ZalgoSanitizer.IsZalgo(parsedItem.Text));

            // Check that the original ID, or a generated one if Guid.Empty, and IsDone match
            Assert.NotEqual(Guid.Empty, parsedItem.Id);

            // Find corresponding input item. Order is preserved for non-empty items.
            var inputItemsWithText = items.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
            var correspondingInput = inputItemsWithText[i];

            if (correspondingInput.Id != Guid.Empty)
            {
                Assert.Equal(correspondingInput.Id, parsedItem.Id);
            }
            Assert.Equal(correspondingInput.IsDone, parsedItem.IsDone);
        }
    }
}
