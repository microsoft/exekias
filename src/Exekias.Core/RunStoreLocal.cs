using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Exekias.Core
{
    public sealed class RunStoreLocal : RunStoreBase
    {
        #region Configuration
        /// <summary>
        /// Configuration options for <see cref="RunStoreLocal"/>.
        /// </summary>
        /// <remarks>
        /// Typically, options come from <c>Microsoft.Extensions.Configuration</c>.
        /// When coupled with <c>Microsoft.Extentions.DependencyInjection</c>, 
        /// the configuration code might look like this:
        /// <c>serviceCollection.Configure&lt;Exekias.Core.RunStoreLocal.Options&gt;(configuration.GetSection(Exekias.Core.RunStoreLocal.OptionsSection))</c>
        /// which binds to a section in a <c>JSON</c> file, e.g.
        /// <c>"RunStore": {"RootDirectory": "C:/Exekias"; "MetadataFilePattern": "^(?&lt;runId&gt;(?&lt;timestamp&gt;[\dT]+)_(?&lt;name&gt;.*))/params.json$"}</c>
        /// </remarks>
        public sealed class Options : OptionsBase
        {
            /// <summary>
            /// Full path to a directory with all the runs.
            /// </summary>
            public string? RootDirectory { get; set; }
        }
        #endregion
        readonly string root;
        public override string AbsoluteBasePath => root;

        /// <summary>
        /// Creates an instance of <see cref="RunStoreLocal"/>.
        /// </summary>
        /// <param name="configurationOptions">An instance of configuration options.</param>
        /// <param name="importer">An instance of <see cref="IImporter"/>.</param>
        /// <param name="logger">An instance of <see cref="ILogger"/>.</param>
        /// <remarks>Typycally, invoked by a Dependency Injection container.</remarks>
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public RunStoreLocal(
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            IOptions<Options> configurationOptions,
            IImporter importer,
            ILogger<RunStoreLocal> logger)
            : base(configurationOptions, importer, logger)
        {
            var options = configurationOptions.Value;
            if (string.IsNullOrWhiteSpace(options.RootDirectory))
                throw new InvalidOperationException("RootDirectory not configured");
            if (!Directory.Exists(options.RootDirectory))
                throw new InvalidOperationException("Must be an existing directory: " + options.RootDirectory);
#pragma warning disable CS8601 // Possible null reference assignment.
            root = options.RootDirectory;
#pragma warning restore CS8601
            logger.LogInformation("Run files are at {0}", root);
        }

        IEnumerable<RunFile> TraverseDirectories(DirectoryInfo dir, string dirPath = "")
        {
            if (dir.Exists)
            {
                var prefix = dirPath == "" ? "" : dirPath + '/';
                foreach (var fi in dir.EnumerateFiles())
                {
                    if (fi.Length > 0)
                    {
                        yield return CreateRunFile(prefix + fi.Name, new DateTimeOffset(fi.LastWriteTime));
                    }
                }
                foreach (var di in dir.EnumerateDirectories())
                {
                    foreach (var item in TraverseDirectories(di, prefix + di.Name))
                    {
                        yield return item;
                    }
                }
            }
        }

        protected override Task<IEnumerable<RunFile>> TraverseAll() =>
            Task.Run(() =>
                TraverseDirectories(new DirectoryInfo(root)));

        protected override IAsyncEnumerable<RunFile> TraverseFilesUnder(string runPath) =>
            TraverseDirectories(new DirectoryInfo(root + '/' + runPath), runPath)
                    .ToAsyncEnumerable();

        /// <summary>
        /// Get local file path.
        /// </summary>
        /// <param name="runFilePath">A path to the file in Run store.</param>
        /// <returns>A <see cref="LocalFile"/> object.</returns>
        public override ValueTask<LocalFile> GetLocalFile(string runFilePath)
        {
            var localFile = new LocalFile(Path.GetFullPath(Path.Combine(root, runFilePath)));
            logger.LogDebug("Accessing local file {0}", localFile.LocalPath);
            return new ValueTask<LocalFile>(localFile);
        }
    }
}
