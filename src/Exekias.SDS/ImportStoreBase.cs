using Exekias.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Research.Science.Data;
using Microsoft.Research.Science.Data.NetCDF4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Exekias.SDS
{
    /// <summary>
    /// Uses NetCDF files to store series data from Run data files.
    /// </summary>
    /// <remarks>
    /// Concrete type must implement <see cref="GetDataSetFile(ExekiasObject)"/>
    /// to define location of the NetCDF summaries.
    /// </remarks>
    public abstract class ImportStoreBase : IImportStore
    {
        public const string OptionsSection = "ImportStore";
        public const string DatasetReuseKey = "dataSetReuse";

        readonly DataImporter importer;
        protected readonly ILogger logger;
        protected ImportStoreBase(DataImporter importer, ILogger logger)
        {
            this.importer = importer ?? throw new ArgumentNullException(nameof(importer));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        /// <summary>
        /// Get a local file reference for an Import Store file.
        /// </summary>
        /// <param name="runObject"></param>
        /// <returns></returns>
        public abstract ValueTask<LocalFile> GetDataSetFile(ExekiasObject runObject);

        public async ValueTask<bool> Import(ValueTask<LocalFile> runFile, ExekiasObject runObject)
        {
            if (null == runFile) throw new ArgumentNullException(nameof(runFile));
            if (null == runObject) throw new ArgumentNullException(nameof(runObject));
            if (null == runObject.Path) throw new ArgumentException("RunFile.Path not initialized.", nameof(runObject));
            var savedVariables = runObject.GetVariables();
            LocalFile localFile = await runFile;
            try
            {
                logger.LogDebug("Start preparing data set for {run}/{path}", runObject.Run, runObject.Path);
                LocalFile dataSetFile = await GetDataSetFile(runObject);
                try
                {
                    Dictionary<string, object> customMetadata = runObject.Meta ?? new Dictionary<string, object>();
                    // reuse is true by default
                    var openMode = ResourceOpenMode.OpenOrCreate;
                    if (0 == savedVariables.Count
                        || (customMetadata.ContainsKey(DatasetReuseKey) && !customMetadata.GetBoolean(DatasetReuseKey)))
                    {
                        openMode = ResourceOpenMode.Create; // do not reuse: If the resource already exists, it will be re-created.
                    }
                    var uri = new NetCDFUri()
                    {
                        FileName = Uri.EscapeDataString(dataSetFile.LocalPath),
                        OpenMode = openMode,
                        EnableRollback = false
                    };
                    logger.LogDebug("Importing to {0}", uri);
                    using var ds = DataSet.Open(uri);
                    ds.IsAutocommitEnabled = false;
                    (string parserName, bool success) = savedVariables.Count > 0
                        ? await importer.Import(
                            localFile.LocalPath, ds, customMetadata,
                            importerName: savedVariables.Keys.First())
                        : await importer.Import(
                            localFile.LocalPath, ds, customMetadata,
                            fileName: runObject.Path);
                    ds.Commit();
                    var variables = new Dictionary<string, string[]>();
                    var variablesArray = ds.Variables.Select(v => v.Name).OrderBy(name => name).ToArray();
                    variables[parserName] = variablesArray;
                    runObject.SetVariables(variables);
                    if (ds.Dimensions.Count > 0)
                    {
                        if (runObject.Meta == null) runObject.Meta = new Dictionary<string, object>();
                        runObject.Meta["dims"] = ds.Dimensions.Select(d => d.Length).ToArray();
                    }
                    var variablesNotChanged = savedVariables.ContainsKey(parserName)
                        && new HashSet<string>(variablesArray).SetEquals(savedVariables[parserName]);
                    logger.LogDebug("Imported {0}, variables changed: {1}", localFile.LocalPath, !variablesNotChanged);
                    return !variablesNotChanged;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "While importing {0}", runObject.Path);
                    throw;
                }
                finally
                {
                    await dataSetFile.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await localFile.DisposeAsync().ConfigureAwait(false);
            }
        }
        public virtual async ValueTask ImportRunData(IRunStore RunStore, IExekiasStore ExekiasStore, string runPath, IEnumerable<FileShot> data)
        {
            bool[] results = await Task.WhenAll(data.Select(async fileTuple =>
            {
                var file = RunStore.CreateRunFile(fileTuple.Path, fileTuple.LastWriteTime);
                var fileObject = new ExekiasObject(runPath, file, forceData: true);
                var variablesChanged = await Import(RunStore.GetLocalFile(file.Path), fileObject);
                await ExekiasStore.PutObject(fileObject);
                return variablesChanged;
            }));
            if (results.Any(v => v))
            {
                var runObject = ExekiasStore.GetMetaObject(runPath);
            }
        }
        public virtual async ValueTask<Dictionary<string, string[]>> GetSummaryMetadata(IExekiasStore exekiasStore, string runPath)
        {
            var accumulator = new Dictionary<string, HashSet<string>>();
            await foreach (var dataObject in exekiasStore.QueryDataObjects(runPath))
            {
                if (dataObject.Type == ExekiasObjectType.Data)
                {
                    var variables = dataObject.GetVariables();
                    foreach (var parserVariables in variables)
                    {
                        if (accumulator.TryGetValue(parserVariables.Key, out var previous))
                        {
                            previous.UnionWith(parserVariables.Value);
                        }
                        else
                        {
                            accumulator.Add(parserVariables.Key, new HashSet<string>(parserVariables.Value));
                        }
                    }
                }
            }
            return accumulator.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }

    }
}
