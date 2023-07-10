using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Exekias.Core
{
    public interface IImporter
    {
        bool CanImport(string path);
    }
    public interface IImportStore
    {
        /// <summary>
        /// Get a local file reference for an Import Store file.
        /// </summary>
        /// <param name="runObject"></param>
        /// <returns></returns>
        ValueTask<LocalFile> GetDataSetFile(ExekiasObject runObject);

        ValueTask<bool> Import(ValueTask<LocalFile> runFile, ExekiasObject runObject);

        ValueTask ImportRunData(IRunStore RunStore, IExekiasStore ExekiasStore, string runPath, IEnumerable<FileShot> data);

        /// <summary>
        /// Collect a summary of import-specific metadata (variables) from existing data objects
        /// into the corresponding runObject.
        /// </summary>
        /// <param name="runId"></param>
        /// <returns></returns>
        ValueTask<Dictionary<string, string[]>> GetSummaryMetadata(IExekiasStore exekiasStore, string runPath);

        /// <summary>
        /// A key under which metadata holds a list of imported variables.
        /// </summary>
        static string VariablesMetaKey = "variables";
    }

    public static class ExekiasObjectVariables
    {
        /// <summary>
        /// Returns variables information if present and an empty dictionary otherwise.
        /// </summary>
        /// <param name="obj">The ExekiasObject instance.</param>
        /// <returns></returns>
        public static Dictionary<string, string[]> GetVariables(this ExekiasObject obj)
        {
            if (obj.Meta.TryGetValue(IImportStore.VariablesMetaKey, out var variablesObject)
                && variablesObject is JsonElement variablesElement)
            {
                var variables = JsonSerializer.Deserialize<Dictionary<string, string[]>>(variablesElement);
                return variables ?? new Dictionary<string, string[]>();
            }
            else
            {
                return new Dictionary<string, string[]>();
            }
        }
        /// <summary>
        /// Sets variables information.
        /// </summary>
        /// <param name="obj">The ExekiasObject instance.</param>
        /// <param name="value">The value to set.</param>
        /// <returns></returns>
        public static void SetVariables(this ExekiasObject obj, Dictionary<string, string[]> value)
        {
            obj.Meta[IImportStore.VariablesMetaKey] = JsonSerializer.SerializeToElement(value);
        }
    }
}
