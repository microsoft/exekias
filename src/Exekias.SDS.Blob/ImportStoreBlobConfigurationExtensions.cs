using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Exekias.SDS.Blob;

namespace Exekias.Core
{
    public static class ImportStoreBlobConfigurationExtensions
    {
        /// <summary>
        /// Adds <see cref="ImportStoreBlob"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationSectionName">Configuration section name with the service options.
        /// The default value is <see cref="ImportStoreBlob.OptionsSection"/> ("ImportStore")</param>
        /// <returns></returns>
        public static IServiceCollection AddImportStoreBlob(
            this IServiceCollection services,
            string configurationSectionName = ImportStoreBlob.OptionsSection)
        {
            services.AddOptions<ImportStoreBlob.Options>()
                .Configure<Microsoft.Extensions.Configuration.IConfiguration>((settings, configuration) =>
                    configuration.GetSection(configurationSectionName)
                    .Bind(settings));
            return services.AddSingleton<IImportStore, ImportStoreBlob>();
        }
    }
}
