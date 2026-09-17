using VaultShared.Seasons;

namespace VaultPreview.SeasonConfigurationInfrastructure;

public sealed class SeasonRevisionDocument
{
    public string Id { get; init; } = string.Empty;
    public SeasonConfiguration Configuration { get; init; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        null,
        []);
    public string RevisionHash { get; init; } = string.Empty;
}
