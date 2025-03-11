using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;

namespace VaultPreview.VaultCache;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVaultCache(this IServiceCollection services)
    {
        services.AddAWSService<IAmazonS3>();

        return services;
    }
}