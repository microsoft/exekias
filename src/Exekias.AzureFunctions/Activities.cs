using Exekias.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace Exekias.AzureFunctions
{
    partial class TriggersAndActivities
    {

        [Function(nameof(ClassifyFiles))]
        public Task<FileClass[]> ClassifyFiles(
            [ActivityTrigger] FileShot[] files)
        {
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
        public static Task<FileClass[]> CallClassifyFilesActivityAsync(TaskOrchestrationContext context, FileShot[] files)
            => context.CallActivityAsync<FileClass[]>(nameof(ClassifyFiles), files);


        [Function(nameof(UpdateRun))]
        public async Task UpdateRun(
            [ActivityTrigger]  RunData runData)
        {
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
        public static Task CallUpdateRunActivityAsync(TaskOrchestrationContext context, RunData run)
            => context.CallActivityAsync(nameof(UpdateRun), run);

        [Function(nameof(FullScan))]
        public async Task<List<RunData>> FullScan(
            [ActivityTrigger] FunctionContext context)
        {
            logger.LogInformation("Starting full scan of Stores");
            List<RunData> imports = await steps.Compare(logger);
            logger.LogInformation("Finished full scan of Stores, {count} runs to import.", imports.Count);
            return imports;
        }

        /// <summary>
        /// Invoke FullScan activity.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="run"></param>
        /// <returns>A string, each character is a corresponing file class, 'M' for metadata and 'D' for importable data.</returns>
        public static Task<List<RunData>> CallFullScanActivityAsync(TaskOrchestrationContext context)
            => context.CallActivityAsync<List<RunData>>(nameof(FullScan));
    }
}
