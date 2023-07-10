using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Exekias.Core
{
    public class Steps
    {
        public IRunStore RunStore { get; }
        public IExekiasStore ExekiasStore { get; }
        public IParamsImporter ExekiasImporter { get; }
        public IImportStore ImportStore { get; }

        readonly ILogger logger;

        /// <summary>
        /// Creates an instance with necessary services configured.
        /// </summary>
        /// <param name="runStore">Run Store implementation.</param>
        /// <param name="exekiasStore">Exekias Store implementation.</param>
        public Steps(
            IRunStore runStore,
            IExekiasStore exekiasStore,
            IParamsImporter exekiasImporter,
            IImportStore importStore,
            ILogger<Steps> logger)
        {
            RunStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
            ExekiasStore = exekiasStore ?? throw new ArgumentNullException(nameof(exekiasStore));
            ExekiasImporter = exekiasImporter ?? throw new ArgumentNullException(nameof(exekiasImporter));
            ImportStore = importStore ?? throw new ArgumentNullException(nameof(importStore));
            this.logger = logger;
        }


        public async ValueTask<List<RunData>> Compare(ILogger? logger = null)
        {
            if (null == logger) logger = this.logger;
            var allRuns = await RunStore.GetAllRuns();
            logger.LogInformation("Total {0} data files in {1} runs.",
                (from kv in allRuns select kv.Value.Data.Count).Sum(), allRuns.Count);
            // Compare with all objects and collect changes
            var obsoleteObjects = new List<ExekiasObject>();
            //var changedRuns = new Dictionary<string, List<RunFile>>();
            //var unchangedMeta = new HashSet<string>();
            var exekiasRuns = new HashSet<string>();
            int objCount = 0;
            await foreach (var runObject in ExekiasStore.GetAllObjects())
            {
                logger.LogDebug("exekias object {0}//{1} {2}", runObject.Run, runObject.Path, runObject.Type);
                objCount += 1;
                var runId = runObject.Run ?? throw new InvalidOperationException("Exekias Object has no Run property.");
                exekiasRuns.Add(runId);
                if (allRuns.TryGetValue(runId, out var runRecord))
                {
                    switch (runObject.Type)
                    {
                        case ExekiasObjectType.Data:
                            var fp = runId + '/' + runObject.Path;
                            var runFile = runRecord.Data.Find(rf => rf.Path == fp);
                            if (null == runFile)
                            {
                                logger.LogWarning("Data file not found for {0}//{1} out of {2} files in the run.",
                                    runId, runObject.Path, runRecord.Data.Count);
                                obsoleteObjects.Add(runObject);
                            }
                            else if (runObject.LastWriteTime >= runFile.LastWriteTime)
                            {
                                runRecord.Data.Remove(runFile);
                                logger.LogDebug("Unchanged data {0}//{1}, {2} data files left in the run.", runId, runObject.Path, runRecord.Data.Count);
                            }
                            break;
                        case ExekiasObjectType.Metadata:
                            if (null != runRecord.Meta && runObject.LastWriteTime >= runRecord.Meta.LastWriteTime)
                            {
                                runRecord.Meta = null;
                                logger.LogDebug("Unchanged meta {0}//{1}.", runId, runObject.Path, runRecord.Data.Count);
                            }
                            break;
                        default:
                            throw new InvalidOperationException("Assertion failure: unsupported object type");
                    }
                }
                else
                {
                    logger.LogWarning("Run {0} not found in Run Store, the Exekias object for {1} is obsolete.",
                        runId, runObject.Path);
                    obsoleteObjects.Add(runObject);
                }
            }
            logger.LogInformation("Found {0} Exekias objects for {1} runs.", objCount, exekiasRuns.Count);
            var changed = allRuns
                .Where(kv => kv.Value.Meta != null || kv.Value.Data.Count > 0)
                .Select(kv => kv.Value.AsData(kv.Key))
                .ToList();
            logger.LogInformation("Changed {0} runs with {1} data files",
                changed.Count,
                (from r in changed select r.Data.Count).Sum());
            logger.LogInformation("Number of obsolete objects is {0}.",
                obsoleteObjects.Count);
            return changed;
        }

        /// <summary>
        /// Update run metadata object in Exekias store.
        /// </summary>
        /// <param name="runPath">A path of a directory in Run store containing all run files.</param>
        /// <param name="file">The run metadata file descriptor to import 
        /// or <c>null</c> if the metadata file has already been imported.</param>
        /// <remarks>
        /// If <paramref name="file"/> is <c>null</c>, Exekias store must contain an up-to-date metadata object for the Run netadata.
        /// </remarks>
        public async ValueTask RunImport(string runPath, RunFile file)
        {
            var runObject = new ExekiasObject(runPath, file);
            var localFile = await RunStore.GetLocalFile(file.Path);
            var parallel = new Task[]{
                ExekiasImporter.Import(localFile.LocalPath).AsTask(),
                ImportStore.GetSummaryMetadata(ExekiasStore, runPath).AsTask()};
            await Task.WhenAll(parallel);
            runObject.Meta[ExekiasObject.RunMetadataFileKey] = ((Task<object>)parallel[0]).Result;
            runObject.SetVariables(((Task<Dictionary<string, string[]>>)parallel[1]).Result);
            await ExekiasStore.PutObject(runObject);
        }

        /// <summary>
        /// Import all updated information for the run,
        /// </summary>
        /// <param name="run">Describes what needs to be imported.</param>
        /// <param name="runPath">A path of a directory in Run store containing all run files.</param>
        /// <param name="dataFiles">All run data files that have no objects in Exekias store or which objects have older last write time.</param>
        /// <param name="metaFile">A metadata file that has no object in Exekias store or which object has older last write time.</param>
        /// <returns>A <see cref="ValueTask"/> structure that represents the work.</returns>
        /// <remarks>
        /// <para>
        ///     Fields of the <paramref name="run"/> value have the following meaning:
        /// </para>
        /// <para>
        ///     <c>runPath</c>: a path of a directory in Run store containing all run files.
        /// </para>
        /// <para>
        ///     <c>Data</c>: a list of data files that have no objects in Exekias store or which objects have older last write time.
        /// </para>
        /// <para>
        ///     <c>Meta</c>: A metadata file that has no object in Exekias store or which object has older last write time.
        /// </para>
        /// </remarks>
        public async ValueTask Import(RunData run)
        {
            var runPath = run.RunPath;
            if (null != run.Meta)
            {
                await RunImport(runPath, RunStore.CreateRunFile(run.Meta.Path, run.Meta.LastWriteTime));
            }
            await ImportStore.ImportRunData(RunStore, ExekiasStore, runPath, run.Data);
        }
    }
}
