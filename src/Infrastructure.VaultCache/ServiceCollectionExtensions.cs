using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using VaultPreview.Blizzard;
using VaultShared.Seasons;

namespace VaultPreview.VaultCache;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVaultCache(this IServiceCollection services)
    {
        services.AddAWSService<IAmazonS3>();
        services.AddSingleton<IJournalMetadataCache, S3JournalMetadataCache>();
        services.AddSingleton<ISeasonAwareDelveBaselineProvider, S3DelveBaselineProvider>();
        services.AddSingleton<S3SeasonRevisionProvider>();
        services.AddSingleton<ISeasonRevisionProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<S3SeasonRevisionProvider>());
        services.AddSingleton<ISeasonRevisionStore>(serviceProvider =>
            serviceProvider.GetRequiredService<S3SeasonRevisionProvider>());
        services.AddSingleton<IActiveSeasonRevisionProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<S3SeasonRevisionProvider>());

        return services;
    }
}
