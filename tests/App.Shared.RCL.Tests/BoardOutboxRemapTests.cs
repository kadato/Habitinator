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
}
