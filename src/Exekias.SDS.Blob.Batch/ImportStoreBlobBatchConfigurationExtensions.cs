using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Exekias.SDS.Blob;
using Exekias.SDS.Blob.Batch;

namespace Exekias.Core
{
    public static class ImportStoreBlobBatchConfigurationExtensions
    {
        /// <summary>
        /// Adds <see cref="ImportStoreBlob"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationSectionName">Configuration section name with the service options.
        /// The default value is <see cref="ImportStoreBlob.OptionsSection"/> ("ImportStore")</param>
        /// <returns></returns>
        public static IServiceCollection AddImportStoreBlobBatch(
            this IServiceCollection services,
            string configurationSectionName = ImportStoreBlob.OptionsSection,
            string batchConfigurationSectionName = "Batch")
        {
            services.AddOptions<ImportStoreBlob.Options>()
                .Configure<IConfiguration>((settings, configuration) =>
                    configuration.GetSection(configurationSectionName)
                    .Bind(settings));
            services.AddOptions<BatchProcessingOptions>()
                .Configure<IConfiguration>((settings, configuration) =>
                    configuration.GetSection(batchConfigurationSectionName)
                    .Bind(settings));
            return services.AddSingleton<IImportStore, ImportStoreBlobBatch>();
        }
    }
}
