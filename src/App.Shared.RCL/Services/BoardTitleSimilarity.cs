using System.Globalization;
using System.Text;

namespace App.Shared.RCL.Services;

/// <summary>
/// Client-side duplicate-title check mirroring the server rule in
/// <c>BoardPersistenceService.FindSimilarActiveTitlesAsync</c>.
/// </summary>
public static class BoardTitleSimilarity
{
    public static bool IsSimilar(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        // Numbered series such as "Batch 1" and "Batch 2" never match,
        // even when the surrounding text is otherwise identical.
        if (DiffersOnlyByNumber(a, b))
        {
            return false;
        }

        if (a.Length >= 8 && b.Length >= 8
            && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))
        {
            return true;
        }

        return TrigramSimilarity(a, b) >= 0.8;
    }

    public static double TrigramSimilarity(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1;
        }

        var setA = Trigrams(a);
        var setB = Trigrams(b);
        if (setA.Count == 0 || setB.Count == 0)
        {
            return 0;
        }

        var inter = setA.Intersect(setB).Count();
        // Dice coefficient over trigram sets.
        return 2.0 * inter / (setA.Count + setB.Count);
    }

    private static HashSet<string> Trigrams(string normalized)
    {
        // Short strings have too few trigrams to be stable; fall back to the
        // whole string so similarity degrades to exact match.
        if (normalized.Length < 3)
        {
            return [normalized];
        }

        HashSet<string> set = [];
        for (var i = 0; i + 3 <= normalized.Length; i++)
        {
            set.Add(normalized.Substring(i, 3));
        }

        return set;
    }

    private static bool DiffersOnlyByNumber(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return false;
        }

        var strippedA = StripDigits(a);
        var strippedB = StripDigits(b);
        return string.Equals(strippedA, strippedB, StringComparison.Ordinal)
            && !string.Equals(strippedA, a, StringComparison.Ordinal);
    }

    private static string StripDigits(string s) =>
        string.Concat(s.EnumerateRunes().Where(rune => !Rune.IsDigit(rune)));

    public static string Normalize(string title)
    {
        var collapsed = string.Join(" ", title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var decomposed = collapsed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(rune);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
