using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Research.Science.Data;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exekias.SDS
{
    /// <summary>
    /// Converts certain kind of files to internal <see cref="DataSet"/> representation.
    /// </summary>
    /// <remarks>
    /// Each parser is able to convert files of specific format.
    /// </remarks>
    public interface IDataImporterPart : Core.IImporter
    {
        /// <summary>
        /// Name of file format.
        /// </summary>
        /// <remarks>
        /// Must be unique across all parsers.
        /// </remarks>
        string Name { get; }
        /// <summary>
        /// Read a file and fill in a <see cref="DataSet"/>.
        /// </summary>
        /// <param name="localPath">Full path to the file on local file system.</param>
        /// <param name="target">The <see cref="DataSet"/> to fill.</param>
        /// <param name="metadata">A dictionary to keep arbitrary metadata about the file.</param>
        /// <returns>True if the method has changed <paramref name="metadata"/>.</returns>
        ValueTask<bool> Import(string localPath, DataSet target, Dictionary<string, object> metadata);
    }

    /// <summary>
    /// The default implementation of the interface
    /// </summary>
    public abstract class DataImporterPartBase : IDataImporterPart
    {
        /// <inheritdoc/>
        public string Name { get { return GetType().Name; } }
        /// <inheritdoc/>
        public abstract bool CanImport(string path);
        /// <inheritdoc/>
        public abstract ValueTask<bool> Import(string localPath, DataSet target, Dictionary<string, object> metadata);
    }

    /// <summary>
    /// A service to wrap a collection of IDataImporter services.
    /// </summary>
    public class DataImporter : Core.IImporter
    {
        readonly ImmutableArray<IDataImporterPart> parts;
        readonly ILogger logger;
        public DataImporter(params IDataImporterPart[] parts)
        {
            this.parts = parts.ToImmutableArray();
            this.logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }
        public DataImporter(IServiceProvider services, ILogger<DataImporter> logger)
        {
            if (null == services) throw new ArgumentNullException(nameof(services));
            this.parts = services.GetServices<IDataImporterPart>().ToImmutableArray();
            this.logger = logger;
        }

        public bool CanImport(string path)
            => parts.Any(importer => importer.CanImport(path));

        public async ValueTask<(string, bool)> Import(
            string localPath, DataSet target, Dictionary<string, object> metadata,
            string importerName = "", string fileName = "")
        {
            IDataImporterPart importer;
            if (!string.IsNullOrWhiteSpace(importerName))
            {
                importer = parts.First(importer => importer.Name == importerName);
            }
            else if (!string.IsNullOrWhiteSpace(fileName))
            {
                importer = parts.First(importer => importer.CanImport(fileName));
            }
            else
            {
                throw new InvalidOperationException($"Either {nameof(importerName)} or {nameof(fileName)} must be specified.");
            }
            logger.LogDebug("{importer} starts from {sourcepath} to {targeturi}.", importer.Name, localPath, target.URI);
            bool done = await importer.Import(localPath, target, metadata);
            logger.LogDebug("{importer} finished {done}.", importer.Name, done);
            return (importer.Name, done);
        }
    }
}
