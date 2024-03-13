using Amazon.Lambda.Annotations;
using Microsoft.Extensions.DependencyInjection;
using VaultShared;

namespace VaultPreviewLambda;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlizzardApiHandler, BlizzardApiHandler>();
        services.AddSingleton<IRaiderIoHandler, RaiderIoHandler>();
        services.AddSingleton<ISecretHandler, SecretHandler>();
        services.AddHttpClient();
    }
}