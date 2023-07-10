using Exekias.Core;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Exekias.AzureFunctions
{
    public partial class TriggersAndActivities
    {
        readonly PipelineOptions options;
        readonly IRunStore runStore;
        readonly IImporter importer;
        readonly IExekiasStore exekiasStore;
        readonly Steps steps;
        public TriggersAndActivities(
            IOptions<PipelineOptions> options,
            Steps steps,
            IRunStore runStore,
            IImporter importer,
            IExekiasStore exekiasStore,
            ILogger<TriggersAndActivities> logger)
        {
            this.options = options.Value;
            this.steps = steps;
            this.runStore = runStore;
            this.importer = importer;
            this.exekiasStore = exekiasStore;
        }

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


        [FunctionName(nameof(RunChangeEventSink))]
        public async Task RunChangeEventSink(
            [EventGridTrigger] EventGridEvent incomingEvent,
            [DurableClient] IDurableOrchestrationClient pipeline,
            ILogger log)
        {
            if (incomingEvent.EventType == "Microsoft.Storage.BlobCreated")
            {
                string? fullPath = null;
                if (incomingEvent.Data is StorageBlobCreatedEventData blobCreated)
                {
                    fullPath = blobCreated.Url;
                }
                else if (incomingEvent.Data is Newtonsoft.Json.Linq.JObject jObject)
                {
                    if (jObject.ContainsKey("url"))
                    {
                        fullPath = jObject.Value<string>("url");
                    }
                    if (string.IsNullOrWhiteSpace(fullPath))
                    {
                        log.LogError("The EventGrid data doesn't have property 'url' {0}", jObject.ToString());
                        return;
                    }
                }
                else
                {
                    log.LogError("Incoming event has wrong schema {0}", incomingEvent.Data.GetType().AssemblyQualifiedName);
                    return;
                }
                var basePath = runStore.AbsoluteBasePath;
                if (fullPath.StartsWith(basePath))
                {
                    var path = fullPath[basePath.Length..];
                    var eventTimeTicks = incomingEvent.EventTime.Ticks;
                    var eventTimeTruncated = new DateTime(
                        eventTimeTicks - eventTimeTicks % TimeSpan.TicksPerSecond,
                        incomingEvent.EventTime.Kind);
                    var lastModified = new DateTimeOffset(eventTimeTruncated);
                    await Orchestrator.EnsureStarted(pipeline, log, options);
                    await Orchestrator.RaiseChangeEventAsync(pipeline, new FileShot(path, lastModified));
                    log.LogInformation("Update event for {0} at {1}.", path, lastModified);
                }
                else
                {
                    log.LogDebug("The event is for a blob {0} outside of Run store {1}.", fullPath, basePath);
                }
            }
            else
            {
                log.LogWarning("Received unsupportes event type {0}", incomingEvent.EventType);
            }
        }


        [FunctionName(nameof(Housekeeper))]
        public async Task Housekeeper(
            [TimerTrigger("0 1 2 * * *")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient pipeline,
            ILogger log)
        {
            log.LogDebug($"{nameof(Housekeeper)} timer trigger function executed at: {DateTime.Now} {myTimer.ScheduleStatus.Last}");
            await Orchestrator.EnsureStarted(pipeline, log, options);
            await Orchestrator.RaiseFullEventAsync(pipeline);
            log.LogInformation("Registered full scan.");
        }



        [FunctionName("Debug")]
        public async Task<string> Debug(
            [HttpTrigger(AuthorizationLevel.Admin, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            DurableOrchestrationStatus? status = null;
            string? command = req.RequestUri.ParseQueryString()["command"];
            if (null == command || "full".Equals(command, StringComparison.InvariantCultureIgnoreCase))
            {
                await Orchestrator.EnsureStarted(starter, log, options);
                await Orchestrator.RaiseFullEventAsync(starter);
                log.LogInformation("Registered full scan.");
            }
            else if ("update".Equals(command, StringComparison.InvariantCultureIgnoreCase))
            {
                if (req.Content == null)
                {
                    log.LogWarning("Expecting path as the request content, ignore the request.");
                }
                else
                {
                    var path = await req.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        log.LogError("Path is empty, ignore the request.");
                    }
                    else
                    {
                        await Orchestrator.EnsureStarted(starter, log, options);
                        await Orchestrator.RaiseChangeEventAsync(starter, new FileShot(path, DateTimeOffset.Now));
                        log.LogInformation("Registered an update for {0}.", path);
                    }
                }
            }
            else if ("restart".Equals(command, StringComparison.InvariantCultureIgnoreCase))
            {
                await starter.RestartAsync(Orchestrator.InstanceId, restartWithNewInstanceId: false);
            }
            else if ("purge".Equals(command, StringComparison.InvariantCultureIgnoreCase))
            {
                status = await starter.GetStatusAsync(Orchestrator.InstanceId);
                if (null == status)
                {
                    log.LogError("Instance didn't start yet.");
                }
                else if (status.RuntimeStatus != OrchestrationRuntimeStatus.Terminated
                    && status.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                    && status.RuntimeStatus != OrchestrationRuntimeStatus.Failed)
                {
                    log.LogWarning("Instance not terminated yet, requesting termination. Wait until the status is Terminated and try purge again.");
                    await starter.TerminateAsync(Orchestrator.InstanceId, "purge requested");
                }
                else
                {
                    var purgeResult = await starter.PurgeInstanceHistoryAsync(Orchestrator.InstanceId);
                    if (purgeResult?.InstancesDeleted > 0)
                    {
                        log.LogInformation("Deleted {number} instances, restarting.", purgeResult?.InstancesDeleted);
                        status = await starter.GetStatusAsync(Orchestrator.InstanceId);
                        if (null == status)
                        {
                            await Orchestrator.EnsureStarted(starter, log, options);
                            log.LogInformation("Started orchestration.");
                        }
                        else
                        {
                            await starter.RestartAsync(Orchestrator.InstanceId, restartWithNewInstanceId: false);
                        }
                    }
                }
            }
            status = await starter.GetStatusAsync(Orchestrator.InstanceId);
            return null == status ? "not started" : status.RuntimeStatus.ToString();
        }
    }


}