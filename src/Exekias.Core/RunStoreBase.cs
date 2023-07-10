using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Exekias.Core
{
    public abstract class RunStoreBase : IRunStore
    {
        #region Configuration
        public const string OptionsSection = "RunStore";
        /// <summary>
        /// Common configuration options for all implementors of RunStoreBase.
        /// </summary>
        public class OptionsBase
        {
            /// <summary>
            /// A regular expression that matches a Run metadata file path.
            /// </summary>
            /// <remarks>
            /// <para>
            /// The path being matched must always use forward slashes as directory separators.
            /// It is a relative path based at the RunStore root.
            /// </para><para>
            /// Named matched subexpressions identify Run metadata fields.
            /// There must always be a subexpression named 'runId'.
            /// It must match the path from the beginning till the name of Run directory.
            /// </para><para>
            /// For example, a Regex 
            /// <c>@&quot;^(?&lt;runId&gt;(?&lt;timestamp&gt;[\dT]+)_(?&lt;name&gt;.*))/params.json$&quot;</c>
            /// configures the run metadata path ending with <c>&quot;/params.json&quot;</c> and having two metadata fields
            /// <c>timestamp</c> and <c>name</c> extracted from the path.
            /// </para>
            /// </remarks>
            // See https://docs.microsoft.com/en-us/dotnet/standard/base-types/grouping-constructs-in-regular-expressions#named-matched-subexpressions
            public string? MetadataFilePattern { get; set; }

            /// <summary>
            /// When <c>true</c>, instructs the store to traverse the whole tree and enumerate all importable files.
            /// </summary>
            public bool CacheAll { get; set; } = false;

            /// <summary>
            /// The name of required named matched subexpression in <see cref="MetadataFilePattern"/>.
            /// </summary>
            public const string runPathCaptureName = "runId";
        }
        #endregion
        protected readonly Regex metadataFileParser;
        readonly List<string> captureNames;
        readonly IImporter importer;
        bool isTracked(RunFile item) => item.IsMetadataFile() || importer.CanImport(item.Path);
        protected readonly ILogger logger;
        public RunStoreBase(
            IOptions<OptionsBase>? configurationOptions,
            IImporter importer,
            ILogger? logger)
        {
            this.importer = importer ?? throw new ArgumentNullException(nameof(importer));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (null == configurationOptions) throw new ArgumentNullException(nameof(configurationOptions));
            var options = configurationOptions?.Value ?? throw new ArgumentNullException(nameof(configurationOptions));
            if (string.IsNullOrWhiteSpace(options.MetadataFilePattern))
                throw new InvalidOperationException("MetadataFilePattern not configured");
            logger.LogInformation("Using metadata file pattern {0}", options.MetadataFilePattern);
            metadataFileParser = new Regex(options.MetadataFilePattern);
            captureNames = metadataFileParser.GetGroupNames().ToList();
            if (!captureNames.Contains(OptionsBase.runPathCaptureName))
                throw new InvalidOperationException(
                    $"Must be a regular expression with a capture named '{OptionsBase.runPathCaptureName}'");
            captureNames.Remove("0");
            captureNames.Remove(OptionsBase.runPathCaptureName);
            captureNames.Insert(0, OptionsBase.runPathCaptureName);
            useCache = options.CacheAll;
        }
        protected abstract Task<IEnumerable<RunFile>> TraverseAll();
        private async Task<IEnumerable<RunFile>> allTracked()
        {
            var all = await TraverseAll();
            return all.Where(isTracked).ToImmutableArray();
        }
        protected abstract IAsyncEnumerable<RunFile> TraverseFilesUnder(string runPath);

        readonly bool useCache;
        Task<IEnumerable<RunFile>>? cache = null;

        public abstract string AbsoluteBasePath { get; }

        /// <summary>
        /// Find all importable files under a Run directory except metadata file.
        /// </summary>
        /// <param name="runPath"></param>
        /// <returns></returns>
        public IAsyncEnumerable<RunFile> GetDataFilesUnder(string runPath)
        {
            if (null == runPath) throw new ArgumentNullException(nameof(runPath));
            if (useCache)
            {
                var prefix = runPath == "" ? "" : runPath + '/';
                return GetAllFiles().Result
                    .Where(item =>
                        null != item.Path
                        && item.Path.StartsWith(prefix)
                        && importer.CanImport(item.Path))
                    .ToAsyncEnumerable();
            }
            else
            {
                return TraverseFilesUnder(runPath).Where(item => importer.CanImport(item.Path));
            }
        }


        public Task<IEnumerable<RunFile>> GetAllFiles()
        {
            if (useCache)
            {
                if (null == cache)
                {
                    cache = allTracked();
                }
                return cache;
            }
            return allTracked();
        }

        public async Task<Dictionary<string, RunRecord>> GetAllRuns()
        {
            logger.LogDebug("Start enumerating all files in Run store.");
            var allFiles = await GetAllFiles();
            logger.LogInformation("Run store found {0} files", allFiles.Count());
            // Group all files by runs and filter out unchanged.
            var byRun = new Dictionary<string, RunRecord>();
            foreach (var f in allFiles)
            {
                if (f.IsMetadataFile())
                {
                    var runPath = f.RunPath();
                    var runPathDir = runPath + '/';
                    var subRun = byRun.Keys.FirstOrDefault(r => r.StartsWith(runPathDir));
                    while (null != subRun)
                    {
                        byRun.Remove(subRun);
                        subRun = byRun.Keys.FirstOrDefault(r => r.StartsWith(runPathDir));
                    }
                    if (!byRun.Keys.Any(r => runPath.StartsWith(r + '/')))
                    {
                        byRun[f.RunPath()] = new RunRecord(f);
                    }
                }
            }
            foreach (var rf in allFiles)
            {
                if (importer.CanImport(rf.Path))
                {
                    var key = byRun.Keys.FirstOrDefault(k => rf.Path.StartsWith(k + '/'));
                    // In transient state data files may exist without metadata file. Ignore them for now.
                    if (null != key) byRun[key].Data.Add(rf);
                }
            }
            logger.LogInformation("Found {0} runs.", byRun.Count);
            return byRun;
        }

        public abstract ValueTask<LocalFile> GetLocalFile(string runFilePath);

        public RunFile CreateRunFile(string filePath, DateTimeOffset lastWriteTime)
        {
            return new RunFileRegex(filePath, lastWriteTime, metadataFileParser.Match(filePath), captureNames);
        }
    }

    public sealed class RunFileRegex : RunFile
    {
        readonly Match metadataFileMatch;
        readonly List<string> captureNames;
        public RunFileRegex(string path, DateTimeOffset lastWriteTime, Match metadataFileMatch, List<string> captureNames)
            : base(path, lastWriteTime)
        {
            this.metadataFileMatch = metadataFileMatch;
            this.captureNames = captureNames;
        }
        /// <summary>
        /// Indicates if the file path looks like a run metadata file path.
        /// </summary>
        /// <returns>True if the file is a metadata file path.</returns>
        public override bool IsMetadataFile() => null != metadataFileMatch && metadataFileMatch.Success;

        /// <summary>
        /// Get metadata for a metadata file path.
        /// </summary>
        /// <returns>Metadata dictionary as inferred from the file path.</returns>
        /// <exception cref="InvalidOperationException">The path is not a metadata file path.</exception>
        public override Dictionary<string, object> PathMetadata()
        {
            if (!metadataFileMatch.Success) throw new InvalidOperationException("The path is not a metadata file path.");
            return captureNames
                .Skip(1)
                .Where(name => metadataFileMatch.Groups[name].Success)
                .ToDictionary(name => name, name => (object)metadataFileMatch.Groups[name].Value);
        }

        /// <summary>
        /// Gets run directory for a metadata file path.
        /// </summary>
        /// <returns>Run directory as inferred from the file path.</returns>
        /// <exception cref="InvalidOperationException">The path is not a metadata file path.</exception>
        public override string RunPath()
        {
            if (!metadataFileMatch.Success) throw new InvalidOperationException("The path is not a metadata file path.");
            return metadataFileMatch.Groups[captureNames[0]].Value;
        }
    }
}
