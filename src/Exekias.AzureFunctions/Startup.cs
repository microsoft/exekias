using Exekias.Core;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(Exekias.AzureFunctions.Startup))]
namespace Exekias.AzureFunctions
{
    public class Startup : FunctionsStartup
    {
        // See https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services
                .AddRunStoreBlobs()
                .AddSingleton<IParamsImporter, JsonParamsImporter>()
                .AddExekiasStoreCosmos()
                .AddImportStoreBlobBatch()
                .AddDataImporter()
            //                .AddTransient<SDS.IDataImporterPart, DataImporterPart.OmnetPP>()
            //                .AddTransient<IParamsImporter, OmnetppImporters.ParamsImporter>()
                ;
            builder.Services
                .AddSingleton<Steps>()
                .AddOptions<PipelineOptions>()
                .Configure<IConfiguration>((settings, configuration) =>
                    configuration.GetSection(PipelineOptions.PipelineOptionsSection)
                    .Bind(settings));
        }
    }
}
