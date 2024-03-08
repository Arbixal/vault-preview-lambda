using Amazon.Lambda.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace VaultPreviewLambda;

[LambdaStartup]
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBlizzardApiHandler, BlizzardApiHandler>();
        services.AddSingleton<ISecretHandler, SecretHandler>();
        services.AddHttpClient();
    }
}