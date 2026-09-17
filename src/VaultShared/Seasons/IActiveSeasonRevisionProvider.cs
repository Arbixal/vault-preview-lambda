namespace VaultShared.Seasons;

public interface IActiveSeasonRevisionProvider
{
    Task<ActiveSeasonRevision?> GetActive(CancellationToken cancellationToken = default);
}
