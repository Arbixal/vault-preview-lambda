namespace VaultShared.Seasons;

public interface ISeasonRevisionStore : ISeasonRevisionProvider
{
    Task<SeasonRevision?> GetRevision(
        string seasonId,
        string revisionId,
        CancellationToken cancellationToken = default);

    Task SaveRevision(SeasonRevision revision, CancellationToken cancellationToken = default);

    Task Activate(
        string seasonId,
        string revisionId,
        DateTimeOffset? activatedAt = null,
        CancellationToken cancellationToken = default);

    Task Schedule(
        string seasonId,
        string revisionId,
        DateTimeOffset activationAt,
        CancellationToken cancellationToken = default);

    Task<SeasonSchedule?> GetScheduled(CancellationToken cancellationToken = default);

    Task CancelSchedule(CancellationToken cancellationToken = default);

    Task Rollback(
        string seasonId,
        string revisionId,
        DateTimeOffset? activatedAt = null,
        CancellationToken cancellationToken = default);
}
