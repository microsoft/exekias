using Exekias.Core;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Exekias.AzureFunctions
{
    partial class TriggersAndActivities
    {
        [FunctionName(nameof(DiscoverRun))]
        public async Task<string?> DiscoverRun(
            [ActivityTrigger] string path,
            ILogger logger)
        {
            try
            {
                logger.LogDebug("Discovering run directory for {0}.", path);
                var result = await exekiasStore.GetRun(path);
                logger.LogInformation("Discovered run directory {0} for {1}.", result ?? "(none)", path);
                return result;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Error while discovering run directory for {0}.", path);
                return null;
            }
        }

        /// <summary>
        /// Invoke DiscoverRun activity.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Task<string?> CallDiscoverRunActivityAsync(IDurableOrchestrationContext context, string path)
            => context.CallActivityAsync<string?>(nameof(DiscoverRun), path);

        [FunctionName(nameof(ClassifyFiles))]
        public Task<FileClass[]> ClassifyFiles(
            [ActivityTrigger] IDurableActivityContext context,
            ILogger logger)
        {
            var files = context.GetInput<FileShot[]>();
            return Task.FromResult(files.Select(file =>
            {
                var fileClass = new FileClass();
                var runFile = runStore.CreateRunFile(file.Path, file.LastWriteTime);
                if (runFile.IsMetadataFile())
                    fileClass.RunPath = runFile.RunPath();
                if (importer.CanImport(file.Path))
                    fileClass.IsData = true;
                logger.LogDebug("File class {path} data:{isData}, meta:{RunPath}.", file.Path, fileClass.IsData, fileClass.RunPath);
                return fileClass;
            }).ToArray());
        }

        /// <summary>
        /// Invoke ClassifyFiles activity.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="files"></param>
        /// <returns>A string, each character is a corresponing file class, 'M' for metadata and 'D' for importable data.</returns>
        public static Task<FileClass[]> CallClassifyFilesActivityAsync(IDurableOrchestrationContext context, FileShot[] files)
            => context.CallActivityAsync<FileClass[]>(nameof(ClassifyFiles), files);


        [FunctionName(nameof(UpdateRun))]
        public async Task UpdateRun(
            [ActivityTrigger] IDurableActivityContext context,
            ILogger logger)
        {
            var runData = context.GetInput<RunData>();
            if (null == runData)
            {
                logger.LogError("UpdateRun didn't receive run data");
                return;
            }
            var runPath = runData.RunPath;
            if (null == runData)
            {
                logger.LogError("UpdateRun didn't receive run path");
                return;
            }
            var dataCount = null == runData.Data ? 0 : runData.Data.Count;
            logger.LogInformation("Updating {count} files from run {run}", dataCount, runData.RunPath);
            try
            {
                if (null == runData.Meta)
                {
                    // data file updates for existing run
                    await steps.Import(runData);
                }
                else
                {
                    var metadataObject = await exekiasStore.GetMetaObject(runPath);
                    if (null == metadataObject)
                    {
                        // if metadataFile is new, do full data import discovery.
                        var allDataFiles = await runStore.GetDataFilesUnder(runPath)
                            .Select(file => file.AsTuple())
                            .ToListAsync();
                        logger.LogDebug("New run, total {count} data files found.", allDataFiles.Count);
                        await steps.Import(new RunData(runData.RunPath, runData.Meta, allDataFiles));
                    }
                    else
                    {
                        await steps.Import(runData);
                    }
                }
            }
            catch (Exception error)
            {
                logger.LogError(error, "UpdateRun caught an exception {exc_type}, run update cancelled.", error.GetType().FullName);
            }
        }

        /// <summary>
        /// Invoke UpdateRun activity.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="run"></param>
        /// <returns>A string, each character is a corresponing file class, 'M' for metadata and 'D' for importable data.</returns>
        public static Task CallUpdateRunActivityAsync(IDurableOrchestrationContext context, RunData run)
            => context.CallActivityAsync(nameof(UpdateRun), run);

        [FunctionName(nameof(FullScan))]
        public async Task<List<RunData>> FullScan(
            [ActivityTrigger] IDurableActivityContext context,
            ILogger logger)
        {
            logger.LogInformation("Starting full scan of Stores");
            List<RunData> imports = await steps.Compare(logger);
            logger.LogInformation("Finished full scan of Stores, {0} runs to import.", imports.Count);
            return imports;
        }

        /// <summary>
        /// Invoke FullScan activity.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="run"></param>
        /// <returns>A string, each character is a corresponing file class, 'M' for metadata and 'D' for importable data.</returns>
        public static Task<List<RunData>> CallFullScanActivityAsync(IDurableOrchestrationContext context)
            => context.CallActivityAsync<List<RunData>>(nameof(FullScan), null);
    }
}
