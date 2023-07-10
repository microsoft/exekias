using Azure.Storage.Blobs;
using Exekias.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Exekias.SDS.Blob
{
    public class ImportStoreBlob : ImportStoreBase
    {
        #region Configuration
        public class Options
        {
            public string? ConnectionString { get; set; }
            public string BlobContainerName { get; set; } = "shadow";
        }
        #endregion
        readonly BlobContainerClient container;

        public ImportStoreBlob(
            IOptions<Options> configurationOptions, 
            DataImporter importer,
            ILogger<ImportStoreBlob> logger
            )
            : base(importer, logger)
        {
            if (null == configurationOptions) throw new ArgumentNullException(nameof(configurationOptions));
            var options = configurationOptions.Value;
            if (string.IsNullOrWhiteSpace(options?.ConnectionString)) throw new ArgumentException($"Options.{nameof(Options.ConnectionString)} not configured.");
            if (string.IsNullOrWhiteSpace(options?.BlobContainerName)) throw new ArgumentException($"Options.{nameof(Options.BlobContainerName)} not configured.");
            container = new BlobContainerClient(options.ConnectionString, options.BlobContainerName);
            container.CreateIfNotExists();
            logger.LogDebug("Is 64 bit process: {is64bit}", Environment.Is64BitProcess);
            logger.LogInformation("ImportStore initialized on blobs {account}{container} with netCDF {netcdf}.", container.AccountName, container.Name, NetCDFInterop.NetCDF.nc_inq_libvers());
        }

        public override async ValueTask<LocalFile> GetDataSetFile(ExekiasObject runObject)
        {
            var file = await LocalBlobFile.Create(container, runObject.Run + "/" + runObject.Path, logger);
            return file;
        }
    }
}
