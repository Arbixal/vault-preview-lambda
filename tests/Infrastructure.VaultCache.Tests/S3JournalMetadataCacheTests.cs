using System.Text;
using VaultPreview.Blizzard;
using VaultPreview.Blizzard.Models;
using VaultPreview.VaultCache;
using Xunit;

namespace Infrastructure.VaultCache.Tests;

public class S3JournalMetadataCacheTests
{
    [Fact]
    public async Task Deserializer_IgnoresCorruptContent()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("{not-json"));

        Assert.Null(await S3JournalMetadataCache.Deserialize(stream));
    }

    [Fact]
    public void Validation_RejectsSemanticallyInvalidContent()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        JournalMetadataCacheEntry invalidEntry = new(
            new BlizzardJournalInstance { Id = 0, Name = string.Empty },
            now.AddHours(1),
            now,
            now.AddDays(1));

        Assert.False(S3JournalMetadataCache.IsValid(invalidEntry));
    }
}
