using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.RCL.Tests;

public sealed class BoardTitleSimilarityTests
{
    [Theory]
    [InlineData("Quarterly planning draft", "Quarterly planning draft")]
    [InlineData("Quarterly planning draft", "  quarterly PLANNING Draft  ")]
    [InlineData("Update the résumé file", "Update the resume file")]
    [InlineData("Prepare the release checklist for review", "release checklist")]
    [InlineData("Cafe wall art", "Café wall art")]
    [InlineData("Resume and portfolio polish", "Resume and portfoli polish")]
    public void Similar_Titles_Match(string left, string right) =>
        BoardTitleSimilarity.IsSimilar(left, right).Should().BeTrue();

    [Fact]
    public void TrigramSimilarity_HighOverlap_IsAtLeastThreshold() =>
        BoardTitleSimilarity.TrigramSimilarity("Resume and portfolio polish", "Resume and portfoli polish")
            .Should().BeGreaterThanOrEqualTo(0.8);

    [Theory]
    [InlineData("Batch 1 abc12345", "Batch 2 abc12345")]
    [InlineData("Buy groceries", "Clean garage")]
    [InlineData("Morning walk", "Morning pages")]
    [InlineData("", "Something")]
    public void Distinct_Titles_Do_Not_Match(string left, string right) =>
        BoardTitleSimilarity.IsSimilar(left, right).Should().BeFalse();
}
