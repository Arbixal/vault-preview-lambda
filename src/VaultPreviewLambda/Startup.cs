using Amazon.Lambda.Annotations;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using VaultPreview.Blizzard;
using VaultPreviewLambda.Calculations;
using VaultPreview.RaiderIo;
using VaultPreview.VaultCache;
using VaultShared;
using VaultShared.Seasons;

namespace VaultPreviewLambda;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlizzardApiHandler, BlizzardApiHandler>();
        services.AddSingleton<BlizzardJournalMetadataProvider>();
        services.AddSingleton<VaultProgressCalculator>();
        services.AddSingleton<VersionedProgressService>();
        services.AddSingleton<IRaiderIoHandler, RaiderIoHandler>();
        services.AddSingleton<ISecretHandler, SecretHandler>();
        services.AddSingleton<IVaultCacheHandler, VaultCacheHandler>();
        services.AddVaultCache();
        services.AddHttpClient();
    }
}
