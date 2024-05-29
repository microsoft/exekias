using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Exekias.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Exekias.AzureStores
{
    public class RunStoreBlobContainer : RunStoreBase
    {
        #region Configuration
        /// <summary>
        /// Configuration options for <see cref="RunStoreBlobContainer"/>.
        /// </summary>
        /// <remarks>
        /// Typically, options come from <c>Microsoft.Extensions.Configuration</c>.
        /// When coupled with <c>Microsoft.Extentions.DependencyInjection</c>, 
        /// the configuration code might look like this:
        /// <c>serviceCollection.Configure&lt;Exekias.AzureStores.RunStoreBlobContainer.Options&gt;(configuration.GetSection(Exekias.AzureStores.RunStoreBlobContainer.OptionsSection))</c>
        /// which binds to a section in a <c>JSON</c> file, e.g.
        /// <c>"RunStore": {"BlobContainerName": "exekias"; "MetadataFilePattern": "^(?&lt;runId&gt;(?&lt;timestamp&gt;[\dT]+)_(?&lt;name&gt;.*))/params.json$"}</c>
        /// </remarks>
        public class Options : OptionsBase
        {
            public string? BlobContainerUrl { get; set; }
        }
        #endregion
        virtual protected BlobContainerClient ContainerClient { get; } // 'protected' to enable mocking
        public override string AbsoluteBasePath => ContainerClient.Uri.ToString() + "/";

        /// <summary>
        /// Creates an instance of <see cref="RunStoreBlobContainer"/>.
        /// </summary>
        /// <param name="configurationOptions">An instance of configuration options.</param>
        /// <param name="importStore">An instance of <see cref="IImportStore"/>.</param>
        /// <param name="logger">An instance of <see cref="ILogger"/>.</param>
        /// <remarks>Typycally, invoked by a Dependency Injection container.</remarks>
        public RunStoreBlobContainer(
            IOptions<Options> configurationOptions,
            IImporter importer,
            ILogger<RunStoreBlobContainer> logger)
            : base(configurationOptions, importer, logger)
        {
            var options = configurationOptions.Value;
            if (string.IsNullOrWhiteSpace(options?.BlobContainerUrl))
                throw new InvalidOperationException("BlobContainerUrl not configured");
            var managedIdentity = Environment.GetEnvironmentVariable("AZURE_MANAGED_IDENTITY");
            var credential = managedIdentity == null ? (TokenCredential) new DefaultAzureCredential() : new ManagedIdentityCredential(managedIdentity);
            ContainerClient = new BlobContainerClient(new Uri(options.BlobContainerUrl), credential);
            logger.LogInformation("Runs are in {0}", ContainerClient.Uri);
        }
        protected override async Task<IEnumerable<RunFile>> TraverseAll() =>
            await ContainerClient.GetBlobsAsync()
                    .Where(item => item.Properties.ContentLength > 0)
                    .Select(BlobItemToRunFile)
                    .ToListAsync();

        protected override IAsyncEnumerable<RunFile> TraverseFilesUnder(string runPath) =>
            ContainerClient
                .GetBlobsAsync(prefix: runPath == "" ? "" : runPath + '/')
                .Where(item => item.Properties.ContentLength > 0)  //{
                    //if (item.Properties.ContentLength <= 0)
                    //    return false;
                    //if (item.Properties.ContentLength > 100*1024*1024)
                    //{
                    //    // Azure Function on Consumption plan has only 500MB temp space
                    //    // See https://github.com/projectkudu/kudu/wiki/Understanding-the-Azure-App-Service-file-system#temporary-files
                    //    logger.LogWarning("Blob size > 100MB, ignore {runPath}.", item.Name);
                    //    return false;
                    //}
                    //return true;
                //})
                .Select(BlobItemToRunFile);

        RunFile BlobItemToRunFile(BlobItem item)
        {
            var runFile = CreateRunFile(item.Name, item.Properties.LastModified ?? DateTimeOffset.MinValue);
            logger.LogDebug("RunFile(isMeta={0}) {1}", runFile.IsMetadataFile(), runFile.Path);
            return runFile;
        }

        /// <summary>
        /// Get temporary local file with contents to a Run file blob.
        /// </summary>
        /// <param name="runFilePath">Blob name of the Run file.</param>
        /// <returns>A <see cref="LocalTempFile"/> object.</returns>
        public override async ValueTask<LocalFile> GetLocalFile(string runFilePath)
        {
            var blob = ContainerClient.GetBlobClient(runFilePath);
            var localFile = new LocalTempFile(runFilePath);
            try
            {
                var response = await blob.DownloadAsync();
                using var v = response.Value;
                using (var fs = File.OpenWrite(localFile.LocalPath))
                {
                    await v.Content.CopyToAsync(fs);
                }
                localFile.SetRunFileLastWriteTime(v.Details.LastModified);
                logger.LogDebug("Downloaded {blobLength} bytes from {blobPath} to {localPath}, {lastWriteTime}", v.Details.ContentLength, runFilePath, localFile.LocalPath, localFile.LastWriteTime);
                return localFile;
            }
            catch (Exception)
            {
                await localFile.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
