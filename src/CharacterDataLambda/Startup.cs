using Amazon.Lambda.Annotations;
using Microsoft.Extensions.DependencyInjection;
using VaultPreview.Blizzard;
using VaultPreview.VaultCache;
using VaultShared;
using VaultShared.Seasons;

namespace CharacterDataLambda;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlizzardApiHandler, BlizzardApiHandler>();
        services.AddSingleton<BlizzardJournalMetadataProvider>();
        services.AddSingleton<ISecretHandler, SecretHandler>();
        services.AddSingleton<IActiveSeasonRevisionProvider, EnvironmentActiveSeasonRevisionProvider>();
        services.AddSingleton<IVaultCacheHandler, VaultCacheHandler>();
        services.AddVaultCache();
        services.AddHttpClient();
    }
}
