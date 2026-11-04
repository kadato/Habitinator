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
    public void Similar_Titles_Match(string left, string right) =>
        BoardTitleSimilarity.IsSimilar(left, right).Should().BeTrue();

    [Theory]
    [InlineData("Batch 1 abc12345", "Batch 2 abc12345")]
    [InlineData("Buy groceries", "Clean garage")]
    [InlineData("Morning walk", "Morning pages")]
    [InlineData("", "Something")]
    public void Distinct_Titles_Do_Not_Match(string left, string right) =>
        BoardTitleSimilarity.IsSimilar(left, right).Should().BeFalse();
}
