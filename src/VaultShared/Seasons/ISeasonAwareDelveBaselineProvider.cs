namespace VaultShared.Seasons;

public interface ISeasonAwareDelveBaselineProvider
{
    Task<DelveBaseline?> GetBaseline(string region, string realm, string character);
    Task SaveBaseline(string region, string realm, string character, DelveBaseline baseline);
}
