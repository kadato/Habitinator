using App.Shared.RCL.Models;

namespace App.Shared.Tests;

public sealed class TodoDueRelativeTextTests
{
    private static readonly DateOnly Today = new(2026, 5, 25);

    [Theory]
    [InlineData(-3, "3 days overdue")]
    [InlineData(-1, "1 day overdue")]
    [InlineData(0, "Due today")]
    [InlineData(1, "Due tomorrow")]
    [InlineData(5, "5 days left")]
    [InlineData(30, "30 days left")]
    public void Format_uses_days_for_near_term(int dayOffset, string expected)
    {
        var due = Today.AddDays(dayOffset);
        Assert.Equal(expected, TodoDueRelativeText.Format(due, Today));
    }

    [Fact]
    public void Format_uses_months_for_longer_future_dates()
    {
        Assert.Equal("1 month left", TodoDueRelativeText.Format(new DateOnly(2026, 6, 25), Today));
        Assert.Equal("2 months left", TodoDueRelativeText.Format(new DateOnly(2026, 7, 25), Today));
    }

    [Fact]
    public void Format_uses_months_when_more_than_thirty_days()
    {
        Assert.Equal("1 month left", TodoDueRelativeText.Format(Today.AddDays(45), Today));
    }
}
