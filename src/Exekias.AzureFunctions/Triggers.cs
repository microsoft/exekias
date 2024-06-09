using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Azure.Messaging.EventGrid.SystemEvents;
using Exekias.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.DurableTask.Client;

namespace Exekias.AzureFunctions
{
    public partial class TriggersAndActivities(
        IOptions<PipelineOptions> options,
        Steps steps,
        IRunStore runStore,
        IImporter importer,
        IExekiasStore exekiasStore)
    {
        readonly PipelineOptions options = options.Value;
        readonly IRunStore runStore = runStore;
        readonly IImporter importer = importer;
        readonly IExekiasStore exekiasStore = exekiasStore;
        readonly Steps steps = steps;

        // Defines four functions that can react to changes in blob storage.
        //  (blob change)
        //      -> (EventGrid)
        //          -> RunChangeSink
        //              -> Pipeline.Update(path, datetime)
        //                  -> DiscoverRun
        //                  -> UpdateRun
        //      -> (at night)
        //          -> Housekeeping
        //              -> Pipeline.Full()
        //                  -> FullScan
        //                  -> UpdateRun


        [Function(nameof(RunChangeEventSink))]
        public async Task RunChangeEventSink(
            [EventGridTrigger] Azure.Messaging.EventGrid.EventGridEvent incomingEvent,
            [DurableClient] DurableTaskClient pipeline,
            ILogger logger)
        {
            if (incomingEvent.EventType == "Microsoft.Storage.BlobCreated")
            {
                string fullPath;
                if (incomingEvent.TryGetSystemEventData(out var systemEvent) && systemEvent is StorageBlobCreatedEventData blobCreated)
                {
                        fullPath = blobCreated.Url;
                }
                else
                {
                    logger.LogError("Cannot decode Microsoft.Storage.BlobCreated event");
                    return;
                }
                var basePath = runStore.AbsoluteBasePath;
                if (fullPath.StartsWith(basePath))
                {
                    var path = fullPath[basePath.Length..];
                    var eventTimeTicks = incomingEvent.EventTime.Ticks;
                    // truncate event time to second
                    var lastModified = new DateTimeOffset(
                        eventTimeTicks - eventTimeTicks % TimeSpan.TicksPerSecond,
                        incomingEvent.EventTime.Offset);
                    await Orchestrator.EnsureStarted(pipeline, logger, options);
                    await Orchestrator.RaiseChangeEventAsync(pipeline, new FileShot(path, lastModified));
                    logger.LogInformation("Update event for {path} at {timestamp}.", path, lastModified);
                }
                else
                {
                    logger.LogWarning("The event is for a blob {bolbPath} outside of Run store {storePath}.", fullPath, basePath);
                }
            }
            else
            {
                logger.LogWarning("Received unsupportes event type {eventType}", incomingEvent.EventType);
            }
        }


        [Function(nameof(Housekeeper))]
        public async Task Housekeeper(
            [TimerTrigger("0 1 2 * * *")] TimerInfo myTimer,
            [DurableClient] DurableTaskClient pipeline,
            ILogger log)
        {
            log.LogDebug("Housekeeper timer trigger function executed at: {timeUtc}, last {last}", DateTime.Now, myTimer.ScheduleStatus?.Last);
            await Orchestrator.EnsureStarted(pipeline, log, options);
            await Orchestrator.RaiseFullEventAsync(pipeline);
            log.LogInformation("Registered full scan.");
        }



        [Function("Debug")]
        public async Task<string> Debug(
            [HttpTrigger(AuthorizationLevel.Admin, "get", "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient starter,
            ILogger log, PipelineOptions options)
        {
            OrchestrationMetadata? status = null;
            string? command = req.Query["command"];
            if (null == command || "full".Equals(command, StringComparison.InvariantCultureIgnoreCase))
            {
                await Orchestrator.EnsureStarted(starter, log, options);
                await Orchestrator.RaiseFullEventAsync(starter);
                log.LogInformation("Registered full scan.");
            }
            else if ("update".Equals(command, StringComparison.InvariantCultureIgnoreCase))
            {
                var path = await req.ReadAsStringAsync();
                if (path == null)
                {
                    log.LogWarning("Expecting path as the request content, ignore the request.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        log.LogError("Path is empty, ignore the request.");
                    }
                    else
                    {
                        await Orchestrator.EnsureStarted(starter, log, options);
                        await Orchestrator.RaiseChangeEventAsync(starter, new FileShot(path, DateTimeOffset.Now));
                        log.LogInformation("Registered an update for {path}.", path);
                    }
                }
            }
            else if ("restart".Equals(command, StringComparison.InvariantCultureIgnoreCase))
            {
                await Orchestrator.EnsureStarted(starter, log, options);
            }
            else if ("purge".Equals(command, StringComparison.InvariantCultureIgnoreCase))
            {
                status = await starter.GetInstanceAsync(Orchestrator.InstanceId);
                if (null == status)
                {
                    log.LogError("Instance didn't start yet.");
                }
                else if (status.RuntimeStatus != OrchestrationRuntimeStatus.Terminated
                    && status.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                    && status.RuntimeStatus != OrchestrationRuntimeStatus.Failed)
                {
                    log.LogWarning("Instance not terminated yet, requesting termination. Wait until the status is Terminated and try purge again.");
                    await starter.TerminateInstanceAsync(Orchestrator.InstanceId, "purge requested");
                }
                else
                {
                    var purgeResult = await starter.PurgeInstanceAsync(Orchestrator.InstanceId);
                    if (purgeResult?.PurgedInstanceCount > 0)
                    {
                        log.LogInformation("Deleted {number} instances, restarting.", purgeResult?.PurgedInstanceCount);
                        status = await starter.GetInstanceAsync(Orchestrator.InstanceId);
                        if (null == status)
                        {
                            await Orchestrator.EnsureStarted(starter, log, options);
                            log.LogInformation("Started orchestration.");
                        }
                        else
                        {
                            await Orchestrator.EnsureStarted(starter, log, options);
                        }
                    }
                }
            }
            status = await starter.GetInstanceAsync(Orchestrator.InstanceId);
            return null == status ? "not started" : status.RuntimeStatus.ToString();
        }
    }


}