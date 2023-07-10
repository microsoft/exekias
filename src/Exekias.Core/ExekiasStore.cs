using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Exekias.Core
{
    public interface IExekiasStore
    {
        /// <summary>
        /// Get a single file object.
        /// </summary>
        /// <param name="runId">Path to a run directory.</param>
        /// <param name="path">Local path to a run within the <paramref name="runId"/></param>
        /// <returns>Exekias store object or null if object doesn't exist.</returns>
        ValueTask<ExekiasObject?> GetMetaObject(string runId);

        /// <summary>
        /// Get a single file object.
        /// </summary>
        /// <param name="runId">Path to a run directory.</param>
        /// <param name="path">Local path to a run within the <paramref name="runId"/></param>
        /// <returns>Exekias store object or null if object doesn't exist.</returns>
        ValueTask<ExekiasObject?> GetDataObject(string runId, string path);

        /// <summary>
        /// Put a single file object.
        /// </summary>
        /// <param name="runFile">An exekias object to put to the store.</param>
        ValueTask PutObject(ExekiasObject runFile);

        /// <summary>
        /// Get descriptors of all files in the store.
        /// </summary>
        /// <returns>A promise of list of file descriptors.</returns>
        /// <remarks>The descriptors do not contain extra metadata.</remarks>
        IAsyncEnumerable<ExekiasObject> GetAllObjects();

        /// <summary>
        /// Get descriptors of all files in a run.
        /// </summary>
        /// <param name="run">Run identifier;</param>
        /// <returns></returns>
        IAsyncEnumerable<ExekiasObject> QueryDataObjects(string run, string where = "", string orderBy = "", bool orderAscending = true, int top = 0);

        /// <summary>
        /// Returns a sequence of objects with Run Metadata according to specified criteria.
        /// </summary>
        /// <param name="where">An implementation specific filter expression. The default is an empty string, no filtering.</param>
        /// <param name="orderBy">Property name to order the sequence. The default is an empty string, no ordering.</param>
        /// <param name="orderAscending">True if order should be ascending, otherwise the order is descending. The default is <c>true</c>.</param>
        /// <param name="top">Maximum number of elements to return. Set to <c>0</c> or negative to return all values.</param>
        /// <returns></returns>
        IAsyncEnumerable<ExekiasObject> QueryMetaObjects(string where, string orderBy, bool orderAscending, int top);

        /// <summary>
        /// Find out which run the path belongs to.
        /// </summary>
        /// <param name="path">Run or <c>null</c> if no run found for the path in the store..</param>
        /// <returns></returns>
        ValueTask<string?> GetRun(string path);
    }

    /// <summary>
    /// Query syntax error 
    /// </summary>
    public class ExekiasStoreSyntaxErrorException: ApplicationException
    {
        public string Query { get; private set; }
        public (int, int) Location { get; private set; }

        public ExekiasStoreSyntaxErrorException(string message, string query, (int, int) location)
            : base(message)
        {
            Query = query;
            Location = location;
        }
    }

    public interface IParamsImporter
    {
        ValueTask<object> Import(string localPath);
    }

    public class JsonParamsImporter : IParamsImporter
    {
        public async ValueTask<object> Import(string localPath)
        {
            using var fileStream = System.IO.File.OpenRead(localPath);
            var doc = await JsonDocument.ParseAsync(fileStream);
            return doc.RootElement;

        }
    }

    public enum ExekiasObjectType
    {
        Data,
        Metadata
    }

    public class ExekiasObject
    {
        public const string RunMetadataFileKey = "params";
        public ExekiasObject() { }
        /// <summary>
        /// Creates an instance.
        /// </summary>
        /// <param name="run">Run directory.</param>
        /// <param name="runFile">Run file.</param>
        /// <param name="forceData">If <c>true</c>, the instance type is <c>Data</c> even if <see cref="RunFile.IsMetadataFile"/> method returns <c>true</c>.</param>
        public ExekiasObject(string run, RunFile runFile, bool forceData = false) : this()
        {
            Run = run ?? throw new ArgumentNullException(nameof(run));
            if (null == runFile) throw new ArgumentNullException(nameof(runFile));
            if (!runFile.Path.StartsWith(Run + '/')) throw new InvalidOperationException($"{runFile.Path} is not from {run}");
            Path = runFile.Path.Substring(run.Length + 1);
            LastWriteTime = runFile.LastWriteTime;
            if (!forceData && runFile.IsMetadataFile())
            {
                Type = ExekiasObjectType.Metadata;
                Meta = runFile.PathMetadata();
            }
            else
            {
                Type = ExekiasObjectType.Data;
            }
        }
        /// <summary>
        /// Path in Run Store where all the run files are.
        /// </summary>
        [JsonPropertyName("run")]
        public string? Run { get; set; }
        /// <summary>
        /// Local file path within the <see cref="Run"/>
        /// </summary>
        [JsonPropertyName("path")]
        virtual public string? Path { get; set; }
        /// <summary>
        /// Last write time to the file in Run Store.
        /// </summary>
        [JsonPropertyName("lastWriteTime")]
        public DateTimeOffset LastWriteTime { get; set; }
        /// <summary>
        /// Object type.
        /// </summary>
        [JsonPropertyName("type")]
        virtual public ExekiasObjectType Type { get; set; }


        /// <summary>
        /// Container for additional properties.
        /// </summary>
        /// <remarks>
        /// <para>Supported value types: <c>bool</c>, <c>int</c>, <c>string</c>, <c>string[]</c>,
        /// <see cref="JsonElement"/>.</para>
        /// <para>Use extension methods like <see cref="ExekiasObjectMetaExtensions.GetStringArray(Dictionary{string, object}, string)"/> to get property values.</para>
        /// </remarks>
        [JsonExtensionData]
        public Dictionary<string, object> Meta { get; set; } = new Dictionary<string, object>();
    }

    public static class ExekiasObjectMetaExtensions
    {
        public static bool GetBoolean(this Dictionary<string, object> dict, string key)
        {
            var obj = dict[key];
            if (obj is bool value) return value;
            if (obj is JsonElement elt) return elt.GetBoolean();
            throw new InvalidOperationException($"For {key} expect value type 'bool' or 'JsonElement', found {obj.GetType()}");
        }
        public static int GetInt32(this Dictionary<string, object> dict, string key)
        {
            var obj = dict[key];
            if (obj is int value) return value;
            if (obj is JsonElement elt) return elt.GetInt32();
            throw new InvalidOperationException($"For {key} expect value type 'int' or 'JsonElement', found {obj.GetType()}");
        }
        public static string? GetString(this Dictionary<string, object> dict, string key)
        {
            var obj = dict[key];
            if (null == obj) return null;
            if (obj is string value) return value;
            if (obj is JsonElement elt) return elt.GetString();
            throw new InvalidOperationException($"For {key} expect value type 'string' or 'JsonElement', found {obj.GetType()}");
        }
        public static string[] GetStringArray(this Dictionary<string, object> dict, string key)
        {
            var obj = dict[key];
            if (obj is string[] value) return value;
            if (obj is JsonElement elt) return elt.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
            throw new InvalidOperationException($"For {key} expect value type 'string[]' or 'JsonElement', found {obj.GetType()}");
        }
        public static Dictionary<string, object> GetDictionary(this Dictionary<string, object> dict, string key)
        {
            var obj = dict[key];
            if (obj is Dictionary<string, object> value) return value;
            if (obj is JsonElement elt)
            {
                var result = new Dictionary<string, object>();
                foreach (var item in elt.EnumerateObject()) result[item.Name] = item.Value;
                return result;
            }
            throw new InvalidOperationException($"For {key} expect value type 'Dictionary<string, object>' or 'JsonElement', found {obj.GetType()}");
        }
    }
}
