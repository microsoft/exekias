using Exekias.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Exekias.SDS
{
    /// <summary>
    /// Uses local NetCDF files to store series data from Run data files.
    /// </summary>
    public sealed class ImportStoreLocal : ImportStoreBase
    {
        #region Configuration
        public class Options
        {
            public string? RootDirectory { get; set; }
        }
        #endregion
        readonly string rootPath;

        /// <summary>
        /// Creates an <see cref="ImportStore"/> instance that keeps series data in  
        /// </summary>
        /// <param name="configurationOptions"></param>
        public ImportStoreLocal(
            IOptions<Options>? configurationOptions,
            DataImporter importer,
            ILogger<ImportStoreLocal> logger)
            : base(importer, logger)
        {
            if (null == configurationOptions) throw new ArgumentNullException(nameof(configurationOptions));
            rootPath = configurationOptions.Value.RootDirectory ?? throw new InvalidOperationException("RootDirectory not configured");
            if (!Directory.Exists(rootPath))
                throw new ArgumentException(rootPath + " must be an existing directory");
            this.rootPath = Path.GetFullPath(rootPath);
            logger.LogInformation("Imported data files are at {0}", rootPath);
        }

        public override ValueTask<LocalFile> GetDataSetFile(ExekiasObject runObject)
        {
            var localPath = Path.GetFullPath(Path.Combine(rootPath, runObject.Run, runObject.Path));
            Directory.CreateDirectory(Path.GetDirectoryName(localPath));
            return new ValueTask<LocalFile>(new LocalFile(localPath) { });
        }
    }
}
