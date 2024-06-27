//
// Command line utility to import a single run blob
// to an ImportStore and update corresponding file and run objects in MetadataStore.
//
// > Exekias.DataImport <run> <file>
//
using Microsoft.Extensions.Hosting;
using Exekias.Core;
using Exekias.Core.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using System.Text.Json;
using Azure.Identity;
using Azure.Core;

var runId = args[0];
var runFile = args[1];
// https://learn.microsoft.com/en-us/azure/azure-monitor/app/ilogger#console-application
using var appInsightsChannel = new InMemoryChannel();
try
{
    using IHost host = Host.CreateDefaultBuilder(args)
        .ConfigureServices((hostBuilder, services) =>
        {
            services
            .AddCredentialProvider(
                new ManagedIdentityCredential(
                    Environment.GetEnvironmentVariable("USER_ASSIGNED_MANAGED_IDENTITY")))
            .AddRunStoreBlobs()
            .AddExekiasStoreCosmos()
            .AddImportStoreBlob()
            .AddDataImporter();
            var appInsightsConnectionString = hostBuilder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
            if (!string.IsNullOrEmpty(appInsightsConnectionString))
            {
                services
                .Configure<TelemetryConfiguration>(config =>
                {
                    // https://github.com/microsoft/ApplicationInsights-dotnet/blob/main/LOGGING/src/ILogger/Readme.md
                    config.TelemetryChannel = appInsightsChannel;
                    config.TelemetryInitializers.Add(new DataImportTelemetryInitializer(runId, runFile));
                })
                .AddLogging(builder =>
                {
                    builder.AddApplicationInsights(
                        configureTelemetryConfiguration: (config) => config.ConnectionString = appInsightsConnectionString,
                        configureApplicationInsightsLoggerOptions: (options) => { }
                    );
                });
            }
        })
        .Build();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var runStore = host.Services.GetRequiredService<IRunStore>();
    var importStore = host.Services.GetRequiredService<IImportStore>();
    var exekiasStore = host.Services.GetRequiredService<IExekiasStore>();

    if (runFile == "-canary:write")
    {
        await exekiasStore.PutObject(new ExekiasObject() { Run = runId, Path = "a.json", LastWriteTime = DateTimeOffset.UtcNow, Type = ExekiasObjectType.Metadata });
        return;
    }
    if (runFile == "-canary:read")
    {
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(await exekiasStore.GetMetaObject(runId))));
        return;
    }

    // Download the file from RunStore to a temporary directory
    logger.LogInformation("Processing {file}", runFile);
    var localFile = await runStore.GetLocalFile(runFile);

    // Look up corresponding object in MetadataStore
    var fileObject = await exekiasStore.GetDataObject(runId, runFile);
    if (fileObject != null && fileObject.LastWriteTime >= localFile.LastWriteTime)
    {
        logger.LogInformation("The file has already been processed {runFile}", runFile);
    }
    else
    {

        if (null == fileObject)
        {
            // if the object doesn't exist, create one.
            var file = runStore.CreateRunFile(runFile, localFile.LastWriteTime);
            fileObject = new ExekiasObject(runId, file, forceData: true);
        }
        else
        {
            // if the object does exist, update the LastWriteTime
            fileObject.LastWriteTime = localFile.LastWriteTime;
        }

        // Create internal representation of a data table from the blob in ImportStore
        // and update the metadata object with information about table columns.
        var variablesChanged = await importStore.Import(ValueTask.FromResult(localFile), fileObject);

        // Update the metadata object in the MetadataStore.
        // This automatically updates column summary for the whole Run.
        await exekiasStore.PutObject(fileObject);
    }

    await host.StartAsync();
}
finally
{
    // Explicitly call Flush() followed by Delay, as required in console apps.
    // This ensures that even if the application terminates, telemetry is sent to the back end.
    appInsightsChannel.Flush();
    await Task.Delay(TimeSpan.FromSeconds(1));
}
