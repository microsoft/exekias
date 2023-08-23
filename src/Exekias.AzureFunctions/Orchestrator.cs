using Exekias.Core;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Exekias.AzureFunctions
{
    internal static class Orchestrator
    {
        // This is singleton durable orchestration function,
        // see https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-singletons?tabs=csharp
        public const string InstanceId = "Exekias";

        public static async Task EnsureStarted(IDurableOrchestrationClient pipeline, ILogger log, PipelineOptions options)
        {
            var status = await pipeline.GetStatusAsync(InstanceId);
            if (null == status)
            {
                await pipeline.StartNewAsync(
                    nameof(Pipeline),
                    InstanceId,
                    new PipelineInput()
                    {
                        ThresholdSeconds = options.ThresholdSeconds,
                        RunCacheTimeoutHours = options.RunCacheTimeoutHours
                    });
                log.LogInformation("Started orchestration {instance_id}.", InstanceId);
            }

        }

        // External pipeline events,
        // see https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-external-events?tabs=csharp
        #region Change event
        const string ChangeOperationName = "Change";
        static Task<FileShot> WaitForChangeEvent(IDurableOrchestrationContext context) => context.WaitForExternalEvent<FileShot>(ChangeOperationName);
        /// <summary>
        /// Raise orchestrator Change event.
        /// </summary>
        /// <param name="pipeline">Orchestration client.</param>
        /// <param name="data">Event data.</param>
        /// <returns>A <see cref="Task"/> that completes when the event notification message has been enqueued.</returns>
        /// <remarks>The event notifies the orchestrator that one of the files in the Run Store has been created or changed.</remarks>
        static public Task RaiseChangeEventAsync(IDurableOrchestrationClient pipeline, FileShot data) => pipeline.RaiseEventAsync(InstanceId, ChangeOperationName, data);
        #endregion
        #region Full event
        public const string FullOperationName = "Full";
        static Task WaitForFullEvent(IDurableOrchestrationContext context) => context.WaitForExternalEvent(FullOperationName);
        /// <summary>
        /// Raise orchestrator Full event.
        /// </summary>
        /// <param name="pipeline">Orchestration client.</param>
        /// <param name="data">Event data.</param>
        /// <returns>A <see cref="Task"/> that completes when the event notification message has been enqueued.</returns>
        /// <remarks>The event requests full scan of Run store and Exekias store to fix inconsistencies.</remarks>
        static public Task RaiseFullEventAsync(IDurableOrchestrationClient pipeline) => pipeline.RaiseEventAsync(InstanceId, FullOperationName);
        #endregion region

        /// <summary>
        /// Collects a batch of change notifications and then requests update of changed runs.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        [FunctionName("Pipeline")]
        public static async Task Pipeline(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger logger
            )
        {
            // WARNING!! The Orchestrator code MUST be deterministic.
            // See https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp
            if (InstanceId != context.InstanceId)
            {
                logger.LogError("The singleton orchestrator started with improper InstanceId {instance_id}, exiting.", context.InstanceId);
                return;
            }
            PipelineInput input = context.GetInput<PipelineInput>();

            // We keep a cache of recent run identifiers to minimize the number of costly DiscoverRun invocations.
            Dictionary<string, DateTimeOffset> runCache = input.runCache;

            var changeOperation = WaitForChangeEvent(context);
            var fullOperation = WaitForFullEvent(context);
            // wait first event indefinitely
            var firstEvent = await Task.WhenAny(changeOperation, fullOperation);
            if (firstEvent == fullOperation)
            {
                // First event is a Full event.
                // The event requests full scan of Run store and Exekias store to fix inconsistencies.
                await DoFullScan(context, logger);
            }
            else
            {
                // First event is a Change event.
                // The event notifies the orchestrator that one of the files in the Run Store has been created or changed.
                FileShot fileShot = await changeOperation;
                var batch = new Dictionary<string, DateTimeOffset>();
                batch[fileShot.Path] = fileShot.LastWriteTime;

                // Collect other Change events for ThresholdSeconds seconds.
                var timerCancellation = new CancellationTokenSource();
                var timer = context.CreateTimer(
                    context.CurrentUtcDateTime + TimeSpan.FromSeconds(input.ThresholdSeconds),
                    timerCancellation.Token);
                bool timeOut = false;
                bool fullScanRequested = false;
                do
                {
                    changeOperation = WaitForChangeEvent(context);
                    var nextEvent = await Task.WhenAny(changeOperation, fullOperation, timer);
                    if (nextEvent == changeOperation)
                    {
                        fileShot = await changeOperation;
                        if (!batch.TryGetValue(fileShot.Path, out var previous)
                            || previous < fileShot.LastWriteTime)
                        {
                            batch[fileShot.Path] = fileShot.LastWriteTime;
                        }
                    }
                    else if (nextEvent == fullOperation)
                    {
                        timerCancellation.Cancel();
                        timeOut = true;
                        fullScanRequested = true;
                    }
                    else // nextEvent == timer
                    {
                        timeOut = true;
                    }
                } while (!timeOut);
                // time is over: group updates by runId
                var fileShots = batch.Select(item => new FileShot(item.Key, item.Value)).ToArray();
                var fileClasses = await TriggersAndActivities.CallClassifyFilesActivityAsync(context, fileShots);
                var shotFilePairs = fileShots.Zip(fileClasses).ToList();

                // find all metadata files among the changes.
                var byRun = shotFilePairs
                    .Where(file => !string.IsNullOrEmpty(file.Second.RunPath))
                    .Select(file => new RunData(file.Second.RunPath, file.First, new List<FileShot>()))
                    .ToDictionary(runData => runData.RunPath);
                var data = shotFilePairs
                    .Where(file => file.Second.IsData)
                    .Select(file => file.First)
                    .ToList();

                // group all data files by run.
                var files = new List<FileShot>();
                foreach (var dataFile in data)
                {
                    var run = byRun.Keys.FirstOrDefault(k => dataFile.Path.StartsWith(k + '/'));
                    if (null != run)
                    {
                        byRun[run].Data.Add(dataFile);
                        if (!runCache.TryGetValue(run, out var cached) || cached < dataFile.LastWriteTime)
                            runCache[run] = dataFile.LastWriteTime;
                    }
                    else
                    {
                        run = runCache.Keys.FirstOrDefault(k => dataFile.Path.StartsWith(k + '/'));
                        if (null != run)
                        {
                            byRun[run] = RunData.Create(run, null, dataFile);
                            if (runCache[run] < dataFile.LastWriteTime)
                                runCache[run] = dataFile.LastWriteTime;
                        }
                        else
                        {
                            // Option 1: new run in Run Store, the update event for meta will arrive later.
                            //   In this case UpdateRun will notice the files once meta arrives.
                            // Option 2: data update for an old run (more than 2d, not in cache but in Exekias Store).
                            //   The change will wait until Full Scan.
                            files.Add(dataFile);
                        }
                    }

                }

                if (files.Count > 0)
                    logger.LogInformation(
                        $"Pipeline skips the following updates because no metadata found: {string.Join(", ", files.ConvertAll(f => f.Path))}");

                // start processing and forget the changes
                await Task.WhenAll(byRun.Select(async item =>
                {
                    try
                    {
                        await TriggersAndActivities.CallUpdateRunActivityAsync(context, item.Value);
                    }
                    catch (FunctionFailedException error)
                    {
                        logger.LogError(error, "Error while updating run {run}, ignored.", item.Key);
                    }
                }));

                // remove uptdated run cache values
                var maxTimeSpan = TimeSpan.FromHours(input.RunCacheTimeoutHours);
                runCache = runCache
                    .Where(item => context.CurrentUtcDateTime - item.Value <= maxTimeSpan)
                    .ToDictionary(item => item.Key, item => item.Value);

                if (fullScanRequested)
                {
                    await DoFullScan(context, logger);
                }

            }
            // This is an "eternal" durable orchestration function,
            // see https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-eternal-orchestrations?tabs=csharp
            input.runCache = runCache;
            context.ContinueAsNew(input, preserveUnprocessedEvents: true);
        }

        static async ValueTask DoFullScan(IDurableOrchestrationContext context, ILogger logger)
        {
            var imports = await TriggersAndActivities.CallFullScanActivityAsync(context);
            await Task.WhenAll(imports.Select(async item =>
            {
                try
                {
                    await TriggersAndActivities.CallUpdateRunActivityAsync(context, item);
                }
                catch (FunctionFailedException error)
                {
                    logger.LogError(error, "Error while updating run {run}, ignored.", item.RunPath);
                }
            }));
        }
    }
}
