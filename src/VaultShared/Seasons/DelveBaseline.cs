namespace VaultShared.Seasons;

public sealed record DelveBaseline(
    string SeasonId,
    string Revision,
    string RevisionHash,
    IReadOnlyDictionary<int, int> Completed);
