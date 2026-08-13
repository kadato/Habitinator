using System.Globalization;
using System.Text;

namespace App.Shared.RCL;

/// <summary>
/// Strips excess Unicode combining characters (Zalgo text) from user-supplied strings
/// while preserving legitimate diacritical marks such as accents (é, ñ, ü, etc.).
/// </summary>
/// <remarks>
/// Zalgo text works by stacking hundreds of combining diacritics on a single base character.
/// Normal accented characters use at most 1-2 combining marks. Zalgo typically uses 10-200.
/// This sanitizer limits combining marks to <see cref="MaxCombiningPerBase"/> per grapheme
/// cluster, which neutralises Zalgo while keeping intentional typography intact.
/// </remarks>
public static class ZalgoSanitizer
{
    /// <summary>
    /// Maximum number of combining codepoints allowed per grapheme cluster.
    /// Value of 2 preserves common double-accented characters (e.g. Vietnamese tonal marks)
    /// while eliminating all practical Zalgo stacking.
    /// </summary>
    public const int MaxCombiningPerBase = 2;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="input"/> contains at least one
    /// grapheme cluster with more than <see cref="MaxCombiningPerBase"/> combining marks.
    /// </summary>
    public static bool IsZalgo(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(input);
        while (enumerator.MoveNext())
        {
            if (CountCombining(enumerator.GetTextElement()) > MaxCombiningPerBase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes excess combining diacritical marks from <paramref name="input"/>,
    /// capping each grapheme cluster at <see cref="MaxCombiningPerBase"/> combining codepoints.
    /// Returns the original string instance unchanged when no Zalgo is detected.
    /// Returns <see langword="null"/> when <paramref name="input"/> is <see langword="null"/>.
    /// </summary>
    public static string? Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (!IsZalgo(input))
        {
            return input; // fast path - avoid allocation for clean strings
        }

        var sb = new StringBuilder(input.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(input);

        while (enumerator.MoveNext())
        {
            var cluster = enumerator.GetTextElement();
            sb.Append(TrimCombining(cluster));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Sanitizes Zalgo text and trims whitespace in one step.
    /// Returns <see cref="string.Empty"/> when <paramref name="input"/> is <see langword="null"/>.
    /// This is the recommended overload for non-nullable contexts (e.g. API request fields).
    /// </summary>
    public static string SanitizeAndTrim(string? input)
        => string.IsNullOrWhiteSpace(input) ? string.Empty : (Sanitize(input.Trim()) ?? string.Empty);

    // -- helpers --

    private static int CountCombining(string cluster)
    {
        var count = 0;
        var index = 0;
        while (index < cluster.Length)
        {
            var codepoint = char.ConvertToUtf32(cluster, index);
            index += char.IsSurrogatePair(cluster, index) ? 2 : 1;
            if (IsCombining(codepoint))
            {
                count++;
            }
        }

        return count;
    }

    private static string TrimCombining(string cluster)
    {
        if (CountCombining(cluster) <= MaxCombiningPerBase)
        {
            return cluster;
        }

        // If the cluster is identified as Zalgo, strip ALL combining marks from it.
        var sb = new StringBuilder(cluster.Length);
        var index = 0;

        while (index < cluster.Length)
        {
            var isSurrogate = index + 1 < cluster.Length && char.IsSurrogatePair(cluster, index);
            var codepoint = char.ConvertToUtf32(cluster, index);
            var charLen = isSurrogate ? 2 : 1;

            if (!IsCombining(codepoint))
            {
                sb.Append(cluster, index, charLen);
            }

            index += charLen;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns <see langword="true"/> when the Unicode codepoint belongs to one of the
    /// three mark categories used to create Zalgo stacking effects.
    /// </summary>
    private static bool IsCombining(int codepoint)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codepoint), 0);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }
}
