using Exekias.SDS.Blob.Batch;
using Microsoft.Azure.Batch.Common;
using Microsoft.Azure.Batch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Exekias.Core;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Exekias.SDS.Blob.Batch
{
    public class ImportStoreBlobBatch : ImportStoreBlob
    {
        readonly BatchClient batchClient;
        readonly BatchProcessingOptions options;
        readonly IEnumerable<KeyValuePair<string, string>> jobEnvironment;

        public ImportStoreBlobBatch(
            IOptions<Options> configurationOptions,
            IOptions<BatchProcessingOptions> batchProcessingOptions,
            IConfiguration configuration,
            DataImporter importer,
            ILogger<ImportStoreBlob> logger)
            : base(configurationOptions, importer, logger)
        {
            if (batchProcessingOptions is null) throw new ArgumentNullException(nameof(batchProcessingOptions));
            options = batchProcessingOptions.Value;
            if (options.Endpoint == null)
            {
                throw new InvalidOperationException("Batch service endpoint not configured.");
            }
            // Batch Service 'endpoint' property as returned by ARM is a hostname.
            // BatchClient expects absolute URL as an endpoint.
            // We accept both variants for Options.Endpoint configuration value.
            var endpoint = options.Endpoint.IndexOf(":") < 0 ? "https://" + options.Endpoint : options.Endpoint;
            batchClient = BatchClient.Open(
                new Microsoft.Azure.Batch.Auth.BatchSharedKeyCredentials(
                    endpoint,
                    options.Name,
                    options.AccessKey));
            const string AppInsightsKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            jobEnvironment = (from name in new[] { "RunStore", "ImportStore", "ExekiasCosmos" }
                              from kv in configuration.GetSection(name).AsEnumerable()
                              where kv.Value != null
                              select kv).Append(new KeyValuePair<string, string>(
                                  AppInsightsKey, configuration[AppInsightsKey]
                                  )).ToImmutableArray();
        }

        public override async ValueTask ImportRunData(IRunStore RunStore, IExekiasStore ExekiasStore, string runPath, IEnumerable<FileShot> data)
        {
            await EnsurePoolAndJob(batchClient, options, jobEnvironment);
            string[] files = data.Select(file => file.Path).ToArray();
            if (files.Length <= 0)
            {
                logger.LogWarning("A request to import {runId} with no data files.", runPath);
                return;
            }
            await SubmitTasksAsync(runPath, files, batchClient, options);
        }


        static async Task SubmitJobAsync(
            string runId,
            IList<string> runFiles,
            IEnumerable<KeyValuePair<string, string>> configuration,
            BatchClient batchClient,
            BatchProcessingOptions options)
        {
            var jobId = Regex.Replace(runId, "[^0-9a-zA-Z_-]", "_");
            if (jobId.Length > 44)
            {
                jobId = jobId.Substring(0, 44);
            }
            jobId = $"{jobId}_{DateTimeOffset.UtcNow.UtcTicks}";
            var job = batchClient.JobOperations.CreateJob(jobId, new PoolInformation() { PoolId = options.PoolId });

            job.CommonEnvironmentSettings = configuration.Select(kv => new EnvironmentSetting(kv.Key, kv.Value)).ToList();
            await job.CommitAsync();
            await job.RefreshAsync();

            CloudTask[] tasks = new CloudTask[runFiles.Count];
            for (int i = 0; i < runFiles.Count; i++) tasks[i] = CreateTask(runId, options, runFiles[i], i + 1);
            await job.AddTaskAsync(tasks);

            job.OnAllTasksComplete = OnAllTasksComplete.TerminateJob;
            await job.CommitAsync();
        }
        static async Task CreatePoolIfNotExists(BatchClient batchClient, BatchProcessingOptions options)
        {
            // Create a pool if not exists
            // VM size standard_d1_v2 (Dv2 series) - 1 vCPU, 3.5G mem, 50G temp (SSD) 46MBps read throughput, 750 MBps network bandwidth
            // 1 task per node at a time
            // Image 2022-datacenter-core-smalldisk
            // Node agent batch.node.widnows amd64
            // Application package dataimport 1.0

            CloudPool? pool = null;
            try
            {
                pool = await batchClient.PoolOperations.GetPoolAsync(options.PoolId);
            }
            catch (BatchException exc)
            {
                if (exc.RequestInformation.HttpStatusCode != System.Net.HttpStatusCode.NotFound)
                    throw;
            }
            if (null == pool)
            {
                Console.WriteLine("Creating pool [{0}]...", options.PoolId);

                ImageReference imageReference = new ImageReference(
                    publisher: options.VmImagePublisher,
                    offer: options.VmImageOffer,
                    sku: options.VmImageSKU,
                    version: "latest");

                VirtualMachineConfiguration virtualMachineConfiguration = new VirtualMachineConfiguration(
                    imageReference: imageReference,
                    nodeAgentSkuId: options.VmAgentSKU);

                pool = batchClient.PoolOperations.CreatePool(
                    poolId: options.PoolId,
                    virtualMachineSize: options.VmSize,
                    virtualMachineConfiguration: virtualMachineConfiguration);

                pool.AutoScaleEnabled = true;
                // Default AutoScaleEvaluationInterval is 15 min, minimum 5 min.
                // No activity for >2h => no allocated nodes (dormant).
                // concurrency = min(maxcuncurrency, max pending tasks for 1h).
                // concurrency == 0 => one dedicated node (sleep).
                // concurrency > 0 => one dedicated node and (concurrency - 1) spot nodes.
                // see https://learn.microsoft.com/en-gb/azure/batch/batch-automatic-scaling
                pool.AutoScaleFormula = @"
maxConcurrency = 10;
dormantTimeInterval = 2 * TimeInterval_Hour;
isNotDormant = $PendingTasks.GetSamplePercent(dormantTimeInterval) < 50 ? 1 : max($PendingTasks.GetSample(dormantTimeInterval));
observationTimeInterval = 1 * TimeInterval_Hour;
observedConcurrency = min(
  $PendingTasks.GetSamplePercent(observationTimeInterval) < 50 ? 1 : max(1, $PendingTasks.GetSample(observationTimeInterval)), 
  maxConcurrency);
$TargetDedicatedNodes = isNotDormant ? 1 : 0;
$TargetLowPriorityNodes = observedConcurrency - 1;
$NodeDeallocationOption = taskcompletion;";

                pool.ApplicationPackageReferences = new List<ApplicationPackageReference> {
        new ApplicationPackageReference{
            ApplicationId = options.AppPackageId,
            Version = options.AppPackageVersion }
    };

                await pool.CommitAsync();
            }
        }

        static CloudTask CreateTask(string runId, BatchProcessingOptions options, string runFile, long stamp)
        {
            // Create a task to import
            // TaskID:
            //     A string that uniquely identifies the Task within the Job.
            //     The ID can contain any combination of alphanumeric characters including
            //     hyphens and underscores, and cannot contain more than 64 characters.
            //     The ID is case-preserving and case-insensitive (that is, you may not have
            //     two IDs within a Job that differ only by case).
            var taskId = Regex.Replace(runFile, "[^0-9a-zA-Z_-]", "_");
            // stamp can be as long as 16 hex digits
            if (taskId.Length > 47)
            {
                taskId = taskId.Substring(0, 47);
            }
            taskId = $"{taskId}_{stamp:x}";
            string appPath = $"%AZ_BATCH_APP_PACKAGE_{options.AppPackageId}#{options.AppPackageVersion}%";
            string taskCommandLine = $"cmd /c {appPath}\\{options.AppPackageExe} \"{runId.Replace("\"","\"\"")}\" \"{runFile.Replace("\"", "\"\"")}\"";
            var task = new CloudTask(taskId, taskCommandLine);
            // https://learn.microsoft.com/en-us/azure/batch/batch-user-accounts#run-a-task-as-an-auto-user-with-pool-scope
            task.UserIdentity = new UserIdentity(new AutoUserSpecification(AutoUserScope.Pool));
            return task;
        }
        static async Task EnsurePoolAndJob(BatchClient batchClient, BatchProcessingOptions options, IEnumerable<KeyValuePair<string, string>> configuration)
        {
            // Create a pool if not exists
            // VM size standard_d1_v2 (Dv2 series) - 1 vCPU, 3.5G mem, 50G temp (SSD) 46MBps read throughput, 750 MBps network bandwidth
            // 1 task per node at a time
            // Image 2022-datacenter-core-smalldisk
            // Node agent batch.node.widnows amd64
            // Application package dataimport 1.0

            CloudPool? pool = null;
            try
            {
                pool = await batchClient.PoolOperations.GetPoolAsync(options.PoolId);
            }
            catch (BatchException exc)
            {
                if (exc.RequestInformation.HttpStatusCode != System.Net.HttpStatusCode.NotFound)
                    throw;
            }
            if (null == pool)
            {
                ImageReference imageReference = new ImageReference(
                    publisher: options.VmImagePublisher,
                    offer: options.VmImageOffer,
                    sku: options.VmImageSKU,
                    version: "latest");

                VirtualMachineConfiguration virtualMachineConfiguration = new VirtualMachineConfiguration(
                    imageReference: imageReference,
                    nodeAgentSkuId: options.VmAgentSKU);

                pool = batchClient.PoolOperations.CreatePool(
                    poolId: options.PoolId,
                    virtualMachineSize: options.VmSize,
                    virtualMachineConfiguration: virtualMachineConfiguration);

                pool.AutoScaleEnabled = true;
                // Default AutoScaleEvaluationInterval is 15 min, minimum 5 min.
                // No activity for >2h => no allocated nodes (dormant).
                // concurrency = min(maxcuncurrency, max pending tasks for 1h).
                // concurrency == 0 => one dedicated node (sleep).
                // concurrency > 0 => one dedicated node and (concurrency - 1) spot nodes.
                // see https://learn.microsoft.com/en-gb/azure/batch/batch-automatic-scaling
                pool.AutoScaleFormula = @"
maxConcurrency = 10;
dormantTimeInterval = 2 * TimeInterval_Hour;
isNotDormant = $PendingTasks.GetSamplePercent(dormantTimeInterval) < 50 ? 1 : max($PendingTasks.GetSample(dormantTimeInterval));
observationTimeInterval = 1 * TimeInterval_Hour;
observedConcurrency = min(
  $PendingTasks.GetSamplePercent(observationTimeInterval) < 50 ? 1 : max(1, $PendingTasks.GetSample(observationTimeInterval)), 
  maxConcurrency);
$TargetDedicatedNodes = isNotDormant ? 1 : 0;
$TargetLowPriorityNodes = observedConcurrency - 1;
$NodeDeallocationOption = taskcompletion;";

                pool.ApplicationPackageReferences = new List<ApplicationPackageReference> {
        new ApplicationPackageReference{
            ApplicationId = options.AppPackageId,
            Version = options.AppPackageVersion }
    };

                await pool.CommitAsync();
            }
            CloudJob? job = null;
            try
            {
                job = await batchClient.JobOperations.GetJobAsync(options.PoolId);
            }
            catch (BatchException exc)
            {
                if (exc.RequestInformation.HttpStatusCode != System.Net.HttpStatusCode.NotFound)
                    throw;
            }
            if (null == job)
            {
                job = batchClient.JobOperations.CreateJob(options.PoolId, new PoolInformation() { PoolId = options.PoolId });
                job.CommonEnvironmentSettings = configuration.Select(kv => new EnvironmentSetting(kv.Key, kv.Value)).ToList();
                await job.CommitAsync();
            }
        }
        static async Task SubmitTasksAsync(
            string runId,
            IList<string> runFiles,
            BatchClient batchClient,
            BatchProcessingOptions options)
        {

            CloudTask[] tasks = new CloudTask[runFiles.Count];
            var stamp = DateTimeOffset.UtcNow.UtcTicks;
            for (int i = 0; i < runFiles.Count; i++) tasks[i] = CreateTask(runId, options, runFiles[i], stamp + i);
            await batchClient.JobOperations.AddTaskAsync(options.PoolId, tasks);
        }

    }
}
