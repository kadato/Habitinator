using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class HabitGoalTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(-4, null)]
    [InlineData(8, 8)]
    [InlineData(2_000_000, HabitGoal.MaxTargetCount)]
    public void Normalize_treats_zero_and_negative_as_no_goal(int? input, int? expected)
    {
        HabitGoal.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Progress_is_clamped_and_met_at_the_target()
    {
        var partial = new BoardItem(Guid.NewGuid(), "Water", Counter: 4, TargetCount: 8);
        var met = partial with { Counter = 8 };
        var over = partial with { Counter = 12 };
        var noGoal = partial with { TargetCount = null };

        HabitGoal.ProgressPercent(partial).Should().Be(50);
        HabitGoal.IsMet(partial).Should().BeFalse();
        HabitGoal.ProgressPercent(met).Should().Be(100);
        HabitGoal.IsMet(met).Should().BeTrue();
        HabitGoal.ProgressPercent(over).Should().Be(100);
        HabitGoal.IsMet(over).Should().BeTrue();
        HabitGoal.ProgressPercent(noGoal).Should().Be(0);
        HabitGoal.IsMet(noGoal).Should().BeFalse();
    }
}
