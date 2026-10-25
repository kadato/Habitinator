namespace App.Shared.RCL.Services.CommandPalette;

/// <summary>Fuzzy match scoring for palette items. Higher is better, 0 means no match.</summary>
public static class CommandPaletteScoring
{
    public static int Score(string? text, string query)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var t = text.Trim();
        var q = query.Trim();
        if (q.Length == 0)
        {
            return 0;
        }

        // Exact prefix wins.
        if (t.StartsWith(q, StringComparison.OrdinalIgnoreCase))
        {
            return 1000 + Math.Max(0, 200 - t.Length);
        }

        // Contiguous substring ranks by position and length.
        var idx = t.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return 500 + Math.Max(0, 200 - idx * 10 - t.Length);
        }

        // Subsequence fuzzy match with bonuses for word boundaries.
        var score = FuzzySubsequence(t, q);
        return score;
    }

    public static int ScoreItem(CommandItem item, string query)
    {
        var best = Score(item.Title, query);
        best = Math.Max(best, Score(item.Subtitle, query) - 100);
        best = Math.Max(best, Score(item.Category, query) - 150);
        if (item.Keywords is not null)
        {
            foreach (var k in item.Keywords)
            {
                best = Math.Max(best, Score(k, query) - 50);
            }
        }

        return Math.Max(0, best);
    }

    private static int FuzzySubsequence(string text, string query)
    {
        var ti = 0;
        var qi = 0;
        var score = 0;
        var lastMatch = -2;
        var lowerText = text.ToLowerInvariant();
        var lowerQuery = query.ToLowerInvariant();

        while (ti < lowerText.Length && qi < lowerQuery.Length)
        {
            if (lowerText[ti] == lowerQuery[qi])
            {
                // Bonus for start of string or after separator.
                if (ti == 0 || IsSeparator(lowerText[ti - 1]))
                {
                    score += 40;
                }
                else if (ti == lastMatch + 1)
                {
                    score += 20;
                }
                else
                {
                    score += 8;
                }

                lastMatch = ti;
                qi++;
            }

            ti++;
        }

        if (qi < lowerQuery.Length)
        {
            return 0;
        }

        // Penalize long texts so short titles win.
        score -= Math.Min(100, text.Length);
        return Math.Max(1, score);
    }

    private static bool IsSeparator(char c) =>
        c is ' ' or '-' or '_' or '/' or '.' or ':' or '(' or '[';
}
