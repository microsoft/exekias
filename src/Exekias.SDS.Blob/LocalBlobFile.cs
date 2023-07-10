using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Exekias.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Exekias.SDS.Blob
{
    /// <summary>
    /// Represents a temporary file that shadows an Azure Storage Blob.
    /// </summary>

    public class LocalBlobFile : LocalTempFile
    {
        readonly BlobClient blob;
        readonly ILogger logger;
        LocalBlobFile(BlobContainerClient container, string runFilePath, ILogger logger)
            : base(runFilePath)
        {
            this.blob = container.GetBlobClient(runFilePath);
            this.logger = logger;
        }

        /// <summary>
        /// Creates an instance of <see cref="LocalBlobFile"/> and downloads
        /// </summary>
        /// <param name="container"></param>
        /// <param name="runFilePath"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public static async ValueTask<LocalBlobFile> Create(BlobContainerClient container, string runFilePath, ILogger logger)
        {
            var instance = new LocalBlobFile(container, runFilePath, logger);
            try
            {
                var response = await instance.blob.DownloadToAsync(instance.LocalPath);
                logger.LogDebug("Existing data set {blobPath} to {localPath}", instance.blob.Uri, instance.LocalPath);
                return instance;
            }
            catch(RequestFailedException error)
            {
                if (error.Status == (int)HttpStatusCode.NotFound)
                {
                    return instance;
                }
                throw;
            }
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            logger.LogDebug("Uploading {blob} from {path}", blob.Uri, LocalPath);
            await blob.UploadAsync(LocalPath, overwrite: true);
            logger.LogDebug("Upload complete.");
            await base.DisposeAsyncCore();
        }
    }
}
