using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Amazon.Lambda.Annotations;
using Microsoft.Extensions.DependencyInjection;
using VaultPreview.SeasonConfigurationInfrastructure;
using VaultShared.Seasons;

namespace SeasonActivationLambda;

[LambdaStartup]
public sealed class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAWSService<IAmazonS3>();
        services.AddSingleton<S3SeasonRevisionProvider>();
        services.AddSingleton<ISeasonRevisionStore>(serviceProvider =>
            serviceProvider.GetRequiredService<S3SeasonRevisionProvider>());
    }
}
