using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Exekias.Core
{
    public static class CoreConfigurationExtensions
    {
        /// <summary>
        /// Adds <see cref="ExekiasStoreLocal"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationSectionName">Configuration section name with the service options.
        /// The default value is <see cref="ExekiasStoreLocal.OptionsSection"/> ("ExekiasStore")</param>
        /// <returns></returns>
        public static IServiceCollection AddExekiasStoreLocal(
            this IServiceCollection services,
            string configurationSectionName = ExekiasStoreLocal.OptionsSection)
        {
            services.AddOptions<ExekiasStoreLocal.Options>()
                .Configure<Microsoft.Extensions.Configuration.IConfiguration>((settings, configuration) =>
                    configuration.GetSection(configurationSectionName)
                    .Bind(settings));
            return services.AddSingleton<IExekiasStore, ExekiasStoreLocal>();
        }

        /// <summary>
        /// Adds <see cref="RunStoreLocal"/> singleton to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configurationSectionName">Configuration section name with the service options.
        /// The default value is <see cref="RunStoreLocal.OptionsSection"/> ("RunStore")</param>
        /// <returns></returns>
        public static IServiceCollection AddRunStoreLocal(
            this IServiceCollection services,
            string configurationSectionName = RunStoreLocal.OptionsSection)
        {
            services.AddOptions<RunStoreLocal.Options>()
                .Configure<Microsoft.Extensions.Configuration.IConfiguration>((settings, configuration) =>
                    configuration.GetSection(configurationSectionName)
                    .Bind(settings));
            return services.AddSingleton<IRunStore, RunStoreLocal>();
        }
    }
}
