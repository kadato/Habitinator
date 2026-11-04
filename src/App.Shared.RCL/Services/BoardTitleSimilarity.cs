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

        return a.Length >= 8 && b.Length >= 8
            && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

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
