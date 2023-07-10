using Exekias.CosmosDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Exekias.Core
{
    public static class ExekiasStoreCosmosConfigurationExtentions
    {
        /// <summary>
        /// Adds <see cref="ExekiasStore"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationSectionName">Configuration section name with the service options.
        /// The default value is <see cref="ExekiasStore.OptionsSection"/> ("ExekiasCosmos")</param>
        /// <returns></returns>
        public static IServiceCollection AddExekiasStoreCosmos(
            this IServiceCollection services,
            string configurationSectionName = ExekiasStore.OptionsSection)
        {
            services.AddOptions<ExekiasStore.Options>()
                .Configure<Microsoft.Extensions.Configuration.IConfiguration>((settings, configuration) =>
                    configuration.GetSection(configurationSectionName)
                    .Bind(settings));
            return services.AddSingleton<IExekiasStore, ExekiasStore>();
        }
    }
}
