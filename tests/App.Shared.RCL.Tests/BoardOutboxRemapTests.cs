using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.RCL.Tests;

public sealed class BoardOutboxRemapTests
{
    [Fact]
    public void Remap_rename_payload_swaps_client_id_for_server_id()
    {
        var client = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var server = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var json = JsonSerializer.Serialize(
            new RenameOutboxPayload(BoardSection.Todo, client, "x"),
            BoardOutboxJson.Options);

        var remapped = BoardOutboxPayloadMapper.RemapClientToServerId(
            BoardOutboxOperationKind.Rename,
            json,
            client,
            server);

        var parsed = JsonSerializer.Deserialize<RenameOutboxPayload>(remapped, BoardOutboxJson.Options);
        parsed.Should().NotBeNull();
        parsed.ItemId.Should().Be(server);
        parsed.Title.Should().Be("x");
    }

    [Fact]
    public void Remap_leaves_unrelated_guids_in_rename_payload()
    {
        var client = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var server = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var other = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var json = JsonSerializer.Serialize(
            new RenameOutboxPayload(BoardSection.Habit, other, "t"),
            BoardOutboxJson.Options);

        var remapped = BoardOutboxPayloadMapper.RemapClientToServerId(
            BoardOutboxOperationKind.Rename,
            json,
            client,
            server);

        var parsed = JsonSerializer.Deserialize<RenameOutboxPayload>(remapped, BoardOutboxJson.Options);
        parsed.Should().NotBeNull();
        parsed.ItemId.Should().Be(other);
    }

    [Fact]
    public void Remap_expected_version_updates_the_expected_timestamp()
    {
        var originalTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var newTime = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(
            new RenameOutboxPayload(BoardSection.Todo, Guid.NewGuid(), "x")
            {
                ExpectedServerUpdatedAtUtc = originalTime
            },
            BoardOutboxJson.Options);

        var remapped = BoardOutboxPayloadMapper.RemapExpectedVersion(
            BoardOutboxOperationKind.Rename,
            json,
            newTime);

        var parsed = JsonSerializer.Deserialize<RenameOutboxPayload>(remapped, BoardOutboxJson.Options);
        parsed.Should().NotBeNull();
        parsed.ExpectedServerUpdatedAtUtc.Should().Be(newTime);
    }

    [Fact]
    public void Update_todo_payload_round_trips_repeat_interval_days_through_id_remap()
    {
        var client = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var server = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var json = JsonSerializer.Serialize(
            new UpdateTodoOutboxPayload(client, "Walk", null, null, null, null, SortOrder: 5.5, TodoRepeatIntervalDays: 3),
            BoardOutboxJson.Options);

        var remapped = BoardOutboxPayloadMapper.RemapClientToServerId(
            BoardOutboxOperationKind.UpdateTodo,
            json,
            client,
            server);

        var parsed = JsonSerializer.Deserialize<UpdateTodoOutboxPayload>(remapped, BoardOutboxJson.Options);
        parsed.Should().NotBeNull();
        parsed.ItemId.Should().Be(server);
        parsed.Title.Should().Be("Walk");
        parsed.SortOrder.Should().Be(5.5);
        parsed.TodoRepeatIntervalDays.Should().Be(3);
    }

    [Fact]
    public void Update_todo_payload_round_trips_repeat_interval_days_through_version_remap()
    {
        var originalTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var newTime = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(
            new UpdateTodoOutboxPayload(Guid.NewGuid(), "Walk", null, null, null, null, originalTime, TodoRepeatIntervalDays: 7),
            BoardOutboxJson.Options);

        var remapped = BoardOutboxPayloadMapper.RemapExpectedVersion(
            BoardOutboxOperationKind.UpdateTodo,
            json,
            newTime);

        var parsed = JsonSerializer.Deserialize<UpdateTodoOutboxPayload>(remapped, BoardOutboxJson.Options);
        parsed.Should().NotBeNull();
        parsed.ExpectedServerUpdatedAtUtc.Should().Be(newTime);
        parsed.TodoRepeatIntervalDays.Should().Be(7);
    }

    [Theory]
    [InlineData(BoardOutboxOperationKind.Archive)]
    [InlineData(BoardOutboxOperationKind.Unarchive)]
    public void Archive_unarchive_payload_swaps_client_id_for_server_id(BoardOutboxOperationKind kind)
    {
        var client = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var server = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var json = JsonSerializer.Serialize(
            new SectionItemOutboxPayload(BoardSection.Todo, client),
            BoardOutboxJson.Options);

        var remapped = BoardOutboxPayloadMapper.RemapClientToServerId(kind, json, client, server);

        var parsed = JsonSerializer.Deserialize<SectionItemOutboxPayload>(remapped, BoardOutboxJson.Options);
        parsed.Should().NotBeNull();
        parsed.ItemId.Should().Be(server);
    }

    [Theory]
    [InlineData(BoardOutboxOperationKind.Archive)]
    [InlineData(BoardOutboxOperationKind.Unarchive)]
    public void Archive_unarchive_payload_remaps_expected_version(BoardOutboxOperationKind kind)
    {
        var originalTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var newTime = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(
            new SectionItemOutboxPayload(BoardSection.Habit, Guid.NewGuid())
            {
                ExpectedServerUpdatedAtUtc = originalTime
            },
            BoardOutboxJson.Options);

        var remapped = BoardOutboxPayloadMapper.RemapExpectedVersion(kind, json, newTime);

        var parsed = JsonSerializer.Deserialize<SectionItemOutboxPayload>(remapped, BoardOutboxJson.Options);
        parsed.Should().NotBeNull();
        parsed.ExpectedServerUpdatedAtUtc.Should().Be(newTime);
    }
}
