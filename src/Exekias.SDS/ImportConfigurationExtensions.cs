using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Exekias.SDS;

namespace Exekias.Core
{
    public static class ImportConfigurationExtensions
    {
        /// <summary>
        /// Adds <see cref="ImportStoreLocal"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationSectionName">Configuration section name with the service options.
        /// The default value is <see cref="ImportStoreLocal.OptionsSection"/> ("ImportStore")</param>
        /// <returns></returns>
        public static IServiceCollection AddImportStoreLocal(
            this IServiceCollection services,
            string configurationSectionName = ImportStoreLocal.OptionsSection)
        {
            services.AddOptions<ImportStoreLocal.Options>()
                .Configure<Microsoft.Extensions.Configuration.IConfiguration>((settings, configuration) =>
                    configuration.GetSection(configurationSectionName)
                    .Bind(settings));
            return services.AddSingleton<IImportStore, ImportStoreLocal>();
        }
        /// <summary>
        /// Adds <see cref="DataImporter"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="addDefaultPart">If <c>true</c> (default), adds <see cref="Exekias.DataImporterPart.CSV">, too.</param>
        /// <returns></returns>
        public static IServiceCollection AddDataImporter(
            this IServiceCollection services,
            bool addDefaultPart = true)
        {
            services
                .AddSingleton<Exekias.SDS.DataImporter>()
                .AddSingleton<IImporter>(services =>
                    // reuse existing singleton
                    services.GetService<Exekias.SDS.DataImporter>() ?? throw new System.InvalidOperationException("DataImporter not configured"));
            if (addDefaultPart)
                services
                    .AddTransient<IDataImporterPart, Exekias.DataImporterPart.CSV>();
            return services;
        }
    }
}
