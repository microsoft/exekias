using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Exekias.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Exekias.SDS.Blob
{
    public class ImportStoreBlob : ImportStoreBase
    {
        #region Configuration
        public class Options
        {
            public string? BlobContainerUrl { get; set; }
        }
        #endregion
        readonly BlobContainerClient container;

        public ImportStoreBlob(
            IOptions<Options> configurationOptions,
            DataImporter importer,
            ILogger<ImportStoreBlob> logger,
            TokenCredential credential)
            : base(importer, logger)
        {
            if (null == configurationOptions) throw new ArgumentNullException(nameof(configurationOptions));
            var options = configurationOptions.Value;
            if (string.IsNullOrWhiteSpace(options?.BlobContainerUrl)) throw new ArgumentException($"Options.{nameof(Options.BlobContainerUrl)} not configured.");
            container = options.BlobContainerUrl.StartsWith("§")
                ? new BlobContainerClient("UseDevelopmentStorage=true", options.BlobContainerUrl.Substring(1))
                : new BlobContainerClient(new Uri(options.BlobContainerUrl), credential);
            container.CreateIfNotExists();
            logger.LogDebug("Is 64 bit process: {is64bit}", Environment.Is64BitProcess);
            string netcdfVersion = "unavailable";
            try
            {
                netcdfVersion = NetCDFInterop.NetCDF.nc_inq_libvers();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to load netCDF library.");
            }
            logger.LogInformation("ImportStore initialized on blobs {account}{container} with netCDF {netcdf}.", container.AccountName, container.Name, netcdfVersion);
        }

        public override async ValueTask<LocalFile> GetDataSetFile(ExekiasObject runObject)
        {
            var file = await LocalBlobFile.Create(container, runObject.Run + "/" + runObject.Path, logger);
            return file;
        }
    }
}
