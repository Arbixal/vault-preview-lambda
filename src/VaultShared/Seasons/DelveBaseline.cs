namespace VaultShared.Seasons;

public interface ISeasonAwareDelveBaselineProvider
{
    Task<DelveBaseline?> GetBaseline(string region, string realm, string character);
    Task SaveBaseline(string region, string realm, string character, DelveBaseline baseline);
}

public sealed record DelveBaseline(
    string SeasonId,
    string Revision,
    string RevisionHash,
    IReadOnlyDictionary<int, int> Completed);

public sealed record ActiveSeasonRevision(
    string SeasonId,
    string Revision,
    string RevisionHash,
    int? SourceSeasonId);

public interface IActiveSeasonRevisionProvider
{
    Task<ActiveSeasonRevision?> GetActive(CancellationToken cancellationToken = default);
}

public sealed class EnvironmentActiveSeasonRevisionProvider : IActiveSeasonRevisionProvider
{
    public Task<ActiveSeasonRevision?> GetActive(CancellationToken cancellationToken = default)
    {
        string? seasonId = Environment.GetEnvironmentVariable("VAULT_PREVIEW_SEASON_ID");
        string? revision = Environment.GetEnvironmentVariable("VAULT_PREVIEW_SEASON_REVISION");
        string? revisionHash = Environment.GetEnvironmentVariable("VAULT_PREVIEW_SEASON_REVISION_HASH");
        string? sourceSeasonId = Environment.GetEnvironmentVariable("VAULT_PREVIEW_SOURCE_SEASON_ID");

        if (string.IsNullOrWhiteSpace(seasonId) ||
            string.IsNullOrWhiteSpace(revision) ||
            string.IsNullOrWhiteSpace(revisionHash))
        {
            return Task.FromResult<ActiveSeasonRevision?>(null);
        }

        int? parsedSourceSeasonId = int.TryParse(sourceSeasonId, out int sourceId)
            ? sourceId
            : null;
        return Task.FromResult<ActiveSeasonRevision?>(new ActiveSeasonRevision(
            seasonId,
            revision,
            revisionHash,
            parsedSourceSeasonId));
    }
}
