using VaultShared.Models.Blizzard;

namespace VaultPreviewLambda.Models.Blizzard;

public class BlizzardExpansion
{
    public BlizzardBase Expansion { get; set; }
    public IList<BlizzardInstance> Instances { get; set; }
}