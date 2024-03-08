namespace VaultPreviewLambda.Models;

public class CharacterProgress
{
    public IDictionary<string, BossProgress> Raid { get; set; } = new Dictionary<string, BossProgress>();
    public IList<DungeonRun> Dungeons { get; set; } = new List<DungeonRun>();
}