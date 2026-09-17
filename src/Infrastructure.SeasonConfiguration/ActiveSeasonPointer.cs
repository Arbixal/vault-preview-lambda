namespace VaultPreview.SeasonConfigurationInfrastructure;

public sealed class ActiveSeasonPointer
{
    public string SeasonId { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public string RevisionHash { get; init; } = string.Empty;
    public DateTimeOffset ActivationAt { get; init; }
}
