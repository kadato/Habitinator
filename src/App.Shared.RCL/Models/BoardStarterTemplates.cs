namespace App.Shared.RCL.Models;

public sealed record BoardStarterItem(
    BoardSection Section,
    string Title,
    string? Tags = null,
    HabitResetPeriod? ResetPeriod = null,
    int? TargetCount = null);

public sealed record BoardStarterTemplate(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<BoardStarterItem> Items);

/// <summary>Starter boards offered while the board is empty.</summary>
public static class BoardStarterTemplates
{
    public static IReadOnlyList<BoardStarterTemplate> All { get; } =
    [
        new(
            "morning",
            "Morning routine",
            "Three small dailies to start the day.",
            [
                new(BoardSection.Daily, "Make the bed", "morning"),
                new(BoardSection.Daily, "Drink a glass of water", "morning, health"),
                new(BoardSection.Daily, "10-minute stretch", "morning, health")
            ]),
        new(
            "fitness",
            "Fitness",
            "Move every day and track a hydration goal.",
            [
                new(BoardSection.Daily, "30-minute workout", "fitness"),
                new(BoardSection.Daily, "Walk outside", "fitness"),
                new(BoardSection.Habit, "Drink 8 glasses of water", "health, hydration", HabitResetPeriod.Daily, 8)
            ]),
        new(
            "focus",
            "Focus",
            "A daily deep work block and a weekly planning habit.",
            [
                new(BoardSection.Daily, "Deep work block", "focus"),
                new(BoardSection.Habit, "Plan the week", "planning", HabitResetPeriod.Weekly, 1)
            ])
    ];

    public static BoardStarterTemplate? Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}
