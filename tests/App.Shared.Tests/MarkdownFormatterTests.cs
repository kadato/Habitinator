using App.Shared.RCL.Services;

namespace App.Shared.Tests;

public sealed class MarkdownFormatterTests
{
    [Fact]
    public void Empty_or_null_returns_empty_string()
    {
        Assert.Equal(string.Empty, MarkdownFormatter.ToHtml(null));
        Assert.Equal(string.Empty, MarkdownFormatter.ToHtml("   "));
    }

    [Fact]
    public void Escapes_raw_html_for_safety()
    {
        var input = "<script>alert('xss')</script>";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Formats_bold_and_inline_code()
    {
        var input = "This is **important** and here is `code_call()`.";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.Contains("<strong>important</strong>", html);
        Assert.Contains("<code class=\"ai-inline-code\">code_call()</code>", html);
    }

    [Fact]
    public void Formats_checklists()
    {
        var input = "- [ ] Pending daily\n- [x] Done daily";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.Contains("ai-checklist-item", html);
        Assert.Contains("Pending daily", html);
        Assert.Contains("ai-checklist-item--done", html);
        Assert.Contains("Done daily", html);
    }

    [Fact]
    public void Formats_headers()
    {
        var input = "# Heading 1\n## Heading 2\n### Heading 3";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.Contains("<h3 class=\"ai-md-h3\">Heading 1</h3>", html);
        Assert.Contains("<h4 class=\"ai-md-h4\">Heading 2</h4>", html);
        Assert.Contains("<h5 class=\"ai-md-h5\">Heading 3</h5>", html);
    }

    [Fact]
    public void Formats_code_blocks()
    {
        var input = "```json\n{\"test\": 123}\n```";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.Contains("<pre class=\"ai-code-block\"><code class=\"language-json\">", html);
        Assert.Contains("{&quot;test&quot;: 123}", html);
        Assert.Contains("</code></pre>", html);
    }

    [Fact]
    public void Formats_bullet_and_ordered_lists()
    {
        var input = "- Item 1\n- Item 2\n\n1. First\n2. Second";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.Contains("<ul class=\"ai-md-list\">", html);
        Assert.Contains("<li>Item 1</li>", html);
        Assert.Contains("<ol class=\"ai-md-list\">", html);
        Assert.Contains("<li>First</li>", html);
    }

    [Fact]
    public void Formats_links_and_autolinks_safely()
    {
        var input = "See [docs](https://example.com/guide) or visit https://example.com/raw.";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.Contains("<a class=\"ai-md-link\" href=\"https://example.com/guide\"", html);
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener\"", html);
        Assert.Contains("https://example.com/raw", html);
    }

    [Fact]
    public void Rejects_javascript_links()
    {
        var input = "Click [evil](javascript:alert(1))";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("href=\"javascript", html);
    }

    [Fact]
    public void Formats_tables()
    {
        var input = "| Task | Due |\n| --- | --- |\n| Buy milk | Today |";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.Contains("<table class=\"ai-md-table\">", html);
        Assert.Contains("<th>Task</th>", html);
        Assert.Contains("<td>Buy milk</td>", html);
    }

    [Fact]
    public void Formats_horizontal_rule()
    {
        var html = MarkdownFormatter.ToHtml("Above\n---\nBelow");

        Assert.Contains("<hr class=\"ai-md-hr\" />", html);
    }

    [Fact]
    public void Does_not_link_inside_code_spans()
    {
        var input = "`https://example.com/raw`";
        var html = MarkdownFormatter.ToHtml(input);

        Assert.Contains("<code class=\"ai-inline-code\">https://example.com/raw</code>", html);
        Assert.DoesNotContain("<a ", html);
    }
}
