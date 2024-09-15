using Microsoft.Azure.Batch.Common;
using Microsoft.Azure.Batch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Exekias.Core;
using Exekias.Core.Azure;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Collections.Immutable;
using Azure.Core;
using Microsoft.Azure.Batch.Auth;
using System.Threading;

namespace Exekias.SDS.Blob.Batch
{
    public class ImportStoreBlobBatch : ImportStoreBlob
    {
        readonly BatchClient batchClient;
        readonly BatchProcessingOptions options;
        readonly IEnumerable<KeyValuePair<string, string>> jobEnvironment;

        static TokenRequestContext batchTokenContext = new TokenRequestContext(new string[] { "https://batch.core.windows.net/.default" });
        public ImportStoreBlobBatch(
            IOptions<Options> configurationOptions,
            IOptions<BatchProcessingOptions> batchProcessingOptions,
            IConfiguration configuration,
            DataImporter importer,
            ILogger<ImportStoreBlob> logger,
            ICredentialProvider credentialProvider)
            : base(configurationOptions, importer, logger, credentialProvider)
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
                new BatchTokenCredentials(
                    endpoint,
                    async () => (await credentialProvider.GetCredential().GetTokenAsync(batchTokenContext, CancellationToken.None)).Token
            ));
            const string AppInsightsKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            const string PoolIdentityKey = "POOL_MANAGED_IDENTITY";
            const string ManagedIdentityKey = "USER_ASSIGNED_MANAGED_IDENTITY";
            jobEnvironment =
                (from name in new[] { "RunStore", "ImportStore", "ExekiasCosmos" }
                 from kv in configuration.GetSection(name).AsEnumerable()
                 where kv.Value != null
                 select kv)
                .Append(new KeyValuePair<string, string>(AppInsightsKey, configuration[AppInsightsKey]))
                .Append(new KeyValuePair<string, string>(ManagedIdentityKey, configuration[PoolIdentityKey]))
                .ToImmutableArray();
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
            string taskCommandLine = $"cmd /c {appPath}\\{options.AppPackageExe} \"{runId.Replace("\"", "\"\"")}\" \"{runFile.Replace("\"", "\"\"")}\"";
            var task = new CloudTask(taskId, taskCommandLine)
            {
                // https://learn.microsoft.com/en-us/azure/batch/batch-user-accounts#run-a-task-as-an-auto-user-with-pool-scope
                UserIdentity = new UserIdentity(new AutoUserSpecification(AutoUserScope.Pool)),
                ApplicationPackageReferences = new List<ApplicationPackageReference> { new ApplicationPackageReference() { ApplicationId = options.AppPackageId, Version = options.AppPackageVersion } }
            };
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

            CloudPool pool = await batchClient.PoolOperations.GetPoolAsync(options.PoolId);
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
