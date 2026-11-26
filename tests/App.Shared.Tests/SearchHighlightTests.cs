using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class SearchHighlightTests
{
    [Fact]
    public void Empty_query_returns_encoded_text()
    {
        SearchHighlight.ToHtml("<b>Read</b>", null).Should().Be("&lt;b&gt;Read&lt;/b&gt;");
    }

    [Fact]
    public void Matches_are_case_insensitive_and_wrapped()
    {
        SearchHighlight.ToHtml("Read and read again", "READ")
            .Should().Be("<mark>Read</mark> and <mark>read</mark> again");
    }

    [Fact]
    public void Query_and_text_are_html_encoded()
    {
        SearchHighlight.ToHtml("a < b", "<")
            .Should().Be("a <mark>&lt;</mark> b");
    }

    [Fact]
    public void No_match_returns_encoded_text()
    {
        SearchHighlight.ToHtml("Read", "write").Should().Be("Read");
    }
}
