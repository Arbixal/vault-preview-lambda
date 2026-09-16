using VaultPreview.Blizzard.Models;

namespace VaultPreview.Blizzard;

public sealed record BlizzardJournalMetadata(
    BlizzardJournalInstance Instance,
    bool IsStale,
    DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset StaleUntil);

public sealed record JournalMetadataCacheEntry(
    BlizzardJournalInstance Instance,
    DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset StaleUntil);

public interface IJournalMetadataCache
{
    Task<JournalMetadataCacheEntry?> Get(
        string region,
        string staticNamespace,
        long instanceId);

    Task Put(
        string region,
        string staticNamespace,
        long instanceId,
        JournalMetadataCacheEntry entry);
}
