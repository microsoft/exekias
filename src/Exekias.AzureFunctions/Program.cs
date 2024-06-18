using Exekias.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Exekias.AzureFunctions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddRunStoreBlobs();
        services.AddSingleton<IParamsImporter, JsonParamsImporter>();
        services.AddExekiasStoreCosmos();
        services.AddImportStoreBlobBatch();
        services.AddDataImporter();
        services.AddSingleton<Steps>();
        services.AddOptions<PipelineOptions>()
        .Configure<IConfiguration>((settings, configuration) =>
            configuration.GetSection(PipelineOptions.PipelineOptionsSection)
            .Bind(settings));
    })
    // see https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide?tabs=windows#managing-log-levels
    .ConfigureLogging(logging =>
    {
        logging.Services.Configure<LoggerFilterOptions>(options =>
        {
            LoggerFilterRule? defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName
                == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null)
            {
                options.Rules.Remove(defaultRule);
            }
        });
    })
    .Build();

host.Run();
