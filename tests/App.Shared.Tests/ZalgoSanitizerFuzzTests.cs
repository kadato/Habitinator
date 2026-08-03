using System.Text;

using App.Shared.RCL;

using FsCheck.Xunit;

namespace App.Shared.Tests;

public sealed class ZalgoSanitizerFuzzTests
{
    [Property]
    public void Sanitize_IsIdempotent(string? input)
    {
        var once = ZalgoSanitizer.Sanitize(input);
        var twice = ZalgoSanitizer.Sanitize(once);
        Assert.Equal(once, twice);
    }

    [Property]
    public void Sanitize_PostCondition_IsNotZalgo(string? input)
    {
        var sanitized = ZalgoSanitizer.Sanitize(input);
        if (sanitized != null)
        {
            Assert.False(ZalgoSanitizer.IsZalgo(sanitized));
        }
    }

    [Property]
    public void Sanitize_CleanString_ReturnsSameInstance(string? input)
    {
        if (input != null && !ZalgoSanitizer.IsZalgo(input))
        {
            var sanitized = ZalgoSanitizer.Sanitize(input);
            Assert.Same(input, sanitized);
        }
    }

    [Property]
    public void Sanitize_DoesNotIncreaseLength(string? input)
    {
        if (input != null)
        {
            var sanitized = ZalgoSanitizer.Sanitize(input);
            Assert.True(sanitized!.Length <= input.Length);
        }
    }

    [Property]
    public void SanitizeAndTrim_Matches_SanitizeThenTrim(string? input)
    {
        var direct = ZalgoSanitizer.SanitizeAndTrim(input);
        var indirect = string.IsNullOrWhiteSpace(input)
            ? string.Empty
            : (ZalgoSanitizer.Sanitize(input.Trim()) ?? string.Empty);

        Assert.Equal(direct, indirect);
    }

    [Property]
    public void Sanitize_GenerativeZalgo_StripsExcessCombining(string baseText, int combiningCount)
    {
        if (string.IsNullOrEmpty(baseText))
        {
            return;
        }

        // Normalize combiningCount to a reasonable positive range to prevent huge string sizes
        var count = Math.Min(Math.Abs(combiningCount) % 100, 50);

        var sb = new StringBuilder();
        foreach (var c in baseText)
        {
            sb.Append(c);
            for (var i = 0; i < count; i++)
            {
                // Unicode Combining Diacritical Marks range U+0300 - U+036F
                var mark = (char)(0x0300 + ((Math.Abs(c) + i) % 112));
                sb.Append(mark);
            }
        }

        var input = sb.ToString();
        var sanitized = ZalgoSanitizer.Sanitize(input);

        if (count > ZalgoSanitizer.MaxCombiningPerBase)
        {
            // If the stacked count exceeds MaxCombiningPerBase, IsZalgo must be true
            Assert.True(ZalgoSanitizer.IsZalgo(input));
            // And once sanitized, the output must no longer be Zalgo
            Assert.False(ZalgoSanitizer.IsZalgo(sanitized));
        }
        else
        {
            // If it is clean (within limits), it shouldn't be Zalgo (unless baseText itself was Zalgo)
            if (!ZalgoSanitizer.IsZalgo(baseText))
            {
                Assert.False(ZalgoSanitizer.IsZalgo(input));
                Assert.Equal(input, sanitized);
            }
        }
    }
}
