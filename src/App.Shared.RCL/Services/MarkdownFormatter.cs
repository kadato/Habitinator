using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace App.Shared.RCL.Services;

/// <summary>Lightweight, safe Markdown to HTML formatter for AI chat messages.</summary>
public static partial class MarkdownFormatter
{
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();

        var inCodeBlock = false;
        var inList = false;
        var isOrderedList = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine;

            // Handle fenced code blocks
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inList)
                {
                    sb.Append(isOrderedList ? "</ol>" : "</ul>");
                    inList = false;
                }

                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    var lang = line.TrimStart()[3..].Trim();
                    var langClass = string.IsNullOrWhiteSpace(lang) ? "" : $" class=\"language-{WebUtility.HtmlEncode(lang)}\"";
                    sb.Append(CultureInfo.InvariantCulture, $"<pre class=\"ai-code-block\"><code{langClass}>");
                }
                else
                {
                    inCodeBlock = false;
                    sb.Append("</code></pre>");
                }
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine(WebUtility.HtmlEncode(line));
                continue;
            }

            // Handle empty line
            if (string.IsNullOrWhiteSpace(line))
            {
                if (inList)
                {
                    sb.Append(isOrderedList ? "</ol>" : "</ul>");
                    inList = false;
                }
                sb.Append("<div class=\"ai-md-spacer\"></div>");
                continue;
            }

            // Check for checklists: - [ ] or - [x]
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("* [ ] ", StringComparison.OrdinalIgnoreCase))
            {
                if (inList)
                {
                    sb.Append(isOrderedList ? "</ol>" : "</ul>");
                    inList = false;
                }
                var itemText = FormatInline(trimmed[6..]);
                sb.Append(CultureInfo.InvariantCulture, $"<div class=\"ai-checklist-item\"><span class=\"ai-check-icon\" aria-hidden=\"true\">☐</span> <span>{itemText}</span></div>");
                continue;
            }

            if (trimmed.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("* [x] ", StringComparison.OrdinalIgnoreCase))
            {
                if (inList)
                {
                    sb.Append(isOrderedList ? "</ol>" : "</ul>");
                    inList = false;
                }
                var itemText = FormatInline(trimmed[6..]);
                sb.Append(CultureInfo.InvariantCulture, $"<div class=\"ai-checklist-item ai-checklist-item--done\"><span class=\"ai-check-icon ai-check-icon--checked\" aria-hidden=\"true\">☑</span> <span class=\"ai-checked-text\">{itemText}</span></div>");
                continue;
            }

            // Headers
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                if (inList)
                {
                    sb.Append(isOrderedList ? "</ol>" : "</ul>");
                    inList = false;
                }
                sb.Append(CultureInfo.InvariantCulture, $"<h5 class=\"ai-md-h5\">{FormatInline(trimmed[4..])}</h5>");
                continue;
            }
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inList)
                {
                    sb.Append(isOrderedList ? "</ol>" : "</ul>");
                    inList = false;
                }
                sb.Append(CultureInfo.InvariantCulture, $"<h4 class=\"ai-md-h4\">{FormatInline(trimmed[3..])}</h4>");
                continue;
            }
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                if (inList)
                {
                    sb.Append(isOrderedList ? "</ol>" : "</ul>");
                    inList = false;
                }
                sb.Append(CultureInfo.InvariantCulture, $"<h3 class=\"ai-md-h3\">{FormatInline(trimmed[2..])}</h3>");
                continue;
            }

            // Blockquote
            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                if (inList)
                {
                    sb.Append(isOrderedList ? "</ol>" : "</ul>");
                    inList = false;
                }
                sb.Append(CultureInfo.InvariantCulture, $"<blockquote class=\"ai-md-quote\">{FormatInline(trimmed[2..])}</blockquote>");
                continue;
            }

            // Unordered list item
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                if (!inList || isOrderedList)
                {
                    if (inList)
                    {
                        sb.Append("</ol>");
                    }
                    sb.Append("<ul class=\"ai-md-list\">");
                    inList = true;
                    isOrderedList = false;
                }
                sb.Append(CultureInfo.InvariantCulture, $"<li>{FormatInline(trimmed[2..])}</li>");
                continue;
            }

            // Ordered list item
            var orderedMatch = OrderedListRegex().Match(trimmed);
            if (orderedMatch.Success)
            {
                if (!inList || !isOrderedList)
                {
                    if (inList)
                    {
                        sb.Append("</ul>");
                    }
                    sb.Append("<ol class=\"ai-md-list\">");
                    inList = true;
                    isOrderedList = true;
                }
                var content = trimmed[orderedMatch.Length..];
                sb.Append(CultureInfo.InvariantCulture, $"<li>{FormatInline(content)}</li>");
                continue;
            }

            // Regular paragraph line
            if (inList)
            {
                sb.Append(isOrderedList ? "</ol>" : "</ul>");
                inList = false;
            }

            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"ai-md-p\">{FormatInline(line)}</p>");
        }

        if (inList)
        {
            sb.Append(isOrderedList ? "</ol>" : "</ul>");
        }
        if (inCodeBlock)
        {
            sb.Append("</code></pre>");
        }

        return sb.ToString();
    }

    private static string FormatInline(string text)
    {
        // 1. HTML Encode first to eliminate XSS
        var encoded = WebUtility.HtmlEncode(text);

        // 2. Inline code: `code`
        encoded = InlineCodeRegex().Replace(encoded, "<code class=\"ai-inline-code\">$1</code>");

        // 3. Bold: **text**
        encoded = BoldRegex().Replace(encoded, "<strong>$1</strong>");

        // 4. Italic: *text* or _text_
        encoded = ItalicRegex().Replace(encoded, "<em>$1</em>");

        // 5. Strikethrough: ~~text~~
        encoded = StrikeRegex().Replace(encoded, "<del>$1</del>");

        return encoded;
    }

    [GeneratedRegex(@"^\d+\.\s+")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*([^*]+)\*(?!\*)")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"~~([^~]+)~~")]
    private static partial Regex StrikeRegex();
}
