using Amazon.Lambda.Annotations;
using Microsoft.Extensions.DependencyInjection;
using VaultPreview.Blizzard;
using VaultPreview.VaultCache;
using VaultShared;

namespace CharacterDataLambda;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlizzardApiHandler, BlizzardApiHandler>();
        services.AddSingleton<ISecretHandler, SecretHandler>();
        services.AddSingleton<IVaultCacheHandler, VaultCacheHandler>();
        services.AddVaultCache();
        services.AddHttpClient();
    }
}