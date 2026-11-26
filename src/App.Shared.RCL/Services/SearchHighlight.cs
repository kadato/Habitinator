using System.Net;
using System.Text;

namespace App.Shared.RCL.Services;

/// <summary>Builds safe HTML that wraps case-insensitive query matches in mark elements.</summary>
public static class SearchHighlight
{
    public static string ToHtml(string? text, string? query)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var needle = query?.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return WebUtility.HtmlEncode(text);
        }

        var builder = new StringBuilder(text.Length + 16);
        var position = 0;
        while (position < text.Length)
        {
            var match = text.IndexOf(needle, position, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                builder.Append(WebUtility.HtmlEncode(text[position..]));
                break;
            }

            builder.Append(WebUtility.HtmlEncode(text[position..match]));
            builder.Append("<mark>");
            builder.Append(WebUtility.HtmlEncode(text.Substring(match, needle.Length)));
            builder.Append("</mark>");
            position = match + needle.Length;
        }

        return builder.ToString();
    }
}
