using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class AiChatStorageTests
{
    [Fact]
    public void StorageKey_IsScopedByUser()
    {
        AiChatStorage.StorageKey("alice@example.com")
            .Should().Be("habitinator-ai-threads:alice@example.com");
        AiChatStorage.StorageKey("bob@example.com")
            .Should().Be("habitinator-ai-threads:bob@example.com");
    }

    [Fact]
    public void StorageKey_NormalizesEmailAndFallsBackForGuests()
    {
        AiChatStorage.StorageKey("  Alice@Example.com  ")
            .Should().Be(AiChatStorage.StorageKey("alice@example.com"));
        AiChatStorage.StorageKey(null)
            .Should().Be("habitinator-ai-threads:guest");
    }
}
