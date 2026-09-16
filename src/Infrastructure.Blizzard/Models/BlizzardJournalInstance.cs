namespace VaultPreview.Blizzard.Models;

public class BlizzardJournalInstance
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public IList<BlizzardJournalEncounter> Encounters { get; set; } = [];
}

public class BlizzardJournalEncounter : BlizzardBase
{
}
