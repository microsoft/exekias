using Exekias.AzureStores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Exekias.Core
{
    public static class AzureStoreConfigurationExtensions
    {
        /// <summary>
        /// Adds <see cref="RunStoreBlobContainer"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationSectionName">Configuration section name with the service options.
        /// The default value is <see cref="RunStoreBlobContainer.OptionsSection"/> ("RunStore")</param>
        /// <returns></returns>
        public static IServiceCollection AddRunStoreBlobs(
            this IServiceCollection services,
            string configurationSectionName = RunStoreBlobContainer.OptionsSection)
        {
            services.AddOptions<RunStoreBlobContainer.Options>()
                .Configure<IConfiguration>((settings, configuration) =>
                    configuration.GetSection(configurationSectionName)
                    .Bind(settings));
            return services.AddSingleton<IRunStore, RunStoreBlobContainer>();
        }

        /// <summary>
        /// Adds <see cref="RunStoreFileShare"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationSectionName">Configuration section name with the service options.
        /// The default value is <see cref="RunStoreFileShare.OptionsSection"/> ("ExekiasCosmos")</param>
        /// <returns></returns>
        //public static IServiceCollection AddRunStoreFiles(
        //    this IServiceCollection services,
        //    string configurationSectionName = RunStoreFileShare.OptionsSection)
        //{
        //    services.AddOptions<RunStoreFileShare.Options>()
        //        .Configure<IConfiguration>((settings, configuration) =>
        //            configuration.GetSection(configurationSectionName)
        //            .Bind(settings));
        //    return services.AddSingleton<IRunStore, RunStoreFileShare>();
        //}
    }
}
