namespace VaultPreviewLambda;

internal sealed record RaidBossDefinition(long EncounterId, string Slug);

internal sealed record RaidDefinition(
    long InstanceId,
    IReadOnlyList<RaidBossDefinition> Bosses);

internal sealed record RaidSeasonDefinition(IReadOnlyList<RaidDefinition> Raids);

internal static class RaidCatalog
{
    private static readonly RaidDefinition VaultOfTheIncarnates = new(
        1200,
        [
            new(2480, "eranog"),
            new(2500, "terros"),
            new(2486, "the-primal-council"),
            new(2482, "sennarth"),
            new(2502, "dathea"),
            new(2491, "kurog-grimtotem"),
            new(2493, "broodkeeper-diurna"),
            new(2499, "raszageth-the-storm-eater")
        ]);

    private static readonly RaidDefinition Aberrus = new(
        1208,
        [
            new(2522, "kazzara"),
            new(2529, "the-amalgamation-chamber"),
            new(2530, "the-forgotten-experiments"),
            new(2524, "assault-of-the-zaqali"),
            new(2525, "rashok"),
            new(2532, "the-vigilant-steward"),
            new(2527, "magmorax"),
            new(2523, "echo-of-neltharion"),
            new(2520, "scalecommander-sarkareth")
        ]);

    private static readonly RaidDefinition Amirdrassil = new(
        1207,
        [
            new(2564, "gnarlroot"),
            new(2554, "igira-the-cruel"),
            new(2557, "volcoross"),
            new(2555, "council-of-dreams"),
            new(2553, "larodar"),
            new(2556, "nymue"),
            new(2563, "smolderon"),
            new(2565, "tindral-sageswift"),
            new(2519, "fyrakk-the-blazing")
        ]);

    private static readonly RaidDefinition NerubarPalace = new(
        1273,
        [
            new(2607, "ulgrax-the-devourer"),
            new(2611, "the-bloodbound-horror"),
            new(2599, "sikran"),
            new(2609, "rashanan"),
            new(2612, "broodtwister-ovinax"),
            new(2601, "nexus-princess-kyveza"),
            new(2608, "the-silken-court"),
            new(2602, "queen-ansurek")
        ]);

    private static readonly RaidDefinition LiberationOfUndermine = new(
        1296,
        [
            new(2639, "vexie-and-the-geargrinders"),
            new(2640, "cauldron-of-carnage"),
            new(2641, "rik-reverb"),
            new(2642, "stix-bunkjunker"),
            new(2653, "sprocketmonger-lockenstock"),
            new(2644, "the-one-armed-bandit"),
            new(2645, "mug'zee"),
            new(2646, "chrome-king-gallywix")
        ]);

    private static readonly RaidDefinition ManaforgeOmega = new(
        1302,
        [
            new(2684, "plexus-sentinel"),
            new(2686, "loom'ithar"),
            new(2685, "soulbinder-naazindhri"),
            new(2687, "forgeweaver-araz"),
            new(2688, "the-soul-hunters"),
            new(2747, "fractillus"),
            new(2690, "nexus-king-salhadaar"),
            new(2691, "dimensius")
        ]);

    private static readonly RaidDefinition TheVoidspire = new(
        1307,
        [
            new(2733, "imperator-averzian"),
            new(2734, "vorasius"),
            new(2735, "vaelgor-&-ezzorak"),
            new(2736, "fallen-king-salhadaar"),
            new(2737, "lightblinded-vanguard"),
            new(2738, "crown-of-the-cosmos")
        ]);

    private static readonly RaidDefinition TheDreamrift = new(
        1314,
        [
            new(2795, "chimaerus-the-undreamt-god")
        ]);

    private static readonly RaidDefinition TheTideboundGrotto = new(
        1317,
        [
            new(2849, "nymrissa-wavecaller")
        ]);

    private static readonly RaidDefinition TheVenomousAbyss = new(
        1320,
        [
            new(2888, "nek'zali-the-soulcoiler"),
            new(2874, "entombed-sentinels"),
            new(2882, "vashnik-the-malignant"),
            new(2894, "the-lost-explorers"),
            new(2871, "sszorak"),
            new(2887, "the-twin-fangs"),
            new(2883, "the-coiled-altar"),
            new(2895, "ula'tek")
        ]);

    public static IReadOnlyDictionary<int, RaidSeasonDefinition> Seasons { get; } =
        new Dictionary<int, RaidSeasonDefinition>
        {
            [9] = new RaidSeasonDefinition([VaultOfTheIncarnates]),
            [10] = new RaidSeasonDefinition([Aberrus]),
            [11] = new RaidSeasonDefinition([Amirdrassil]),
            [12] = new RaidSeasonDefinition([VaultOfTheIncarnates, Aberrus, Amirdrassil]),
            [13] = new RaidSeasonDefinition([NerubarPalace]),
            [14] = new RaidSeasonDefinition([LiberationOfUndermine]),
            [15] = new RaidSeasonDefinition([ManaforgeOmega]),
            [16] = new RaidSeasonDefinition([TheVoidspire, TheDreamrift]),
            [17] = new RaidSeasonDefinition([TheVenomousAbyss]),
            [18] = new RaidSeasonDefinition([TheVenomousAbyss, TheTideboundGrotto])
        };
}
