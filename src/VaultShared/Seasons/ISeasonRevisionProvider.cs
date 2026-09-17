namespace VaultShared.Seasons;

public interface ISeasonRevisionProvider
{
    Task<SeasonRevision?> GetActiveRevision(CancellationToken cancellationToken = default);
}
