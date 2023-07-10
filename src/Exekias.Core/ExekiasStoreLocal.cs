using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Exekias.Core
{
    /// <summary>
    /// In-memory implementation.
    /// </summary>
    public class ExekiasStoreLocal : IExekiasStore
    {
        #region Configuration
        public const string OptionsSection = "ExekiasStore";
        public class Options
        {
            public string? DatabasePath { get; set; }
        }
        #endregion
        readonly string databasePath;
        readonly List<ExekiasObject> database;
        private int FindIndex(string run, string? path, ExekiasObjectType type)
        {
            if (null == database) return -1;
            return database.FindIndex(item => item.Run == run && (null == path ? true : item.Path == path) && item.Type == type);
        }

        readonly ILogger logger;
        public ExekiasStoreLocal(
            IOptions<Options> options,
            ILogger<ExekiasStoreLocal> logger)
        {
            databasePath = options?.Value?.DatabasePath ?? throw new InvalidOperationException($"{nameof(Options.DatabasePath)} option not configured for {typeof(Options).FullName}");
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (File.Exists(databasePath))
            {
                var json = File.ReadAllBytes(databasePath);
                database = JsonSerializer.Deserialize<List<ExekiasObject>>(json) ?? new List<ExekiasObject>();
            }
            else
            {
                database = new List<ExekiasObject>();
                Flush();
            }
        }

        private void Flush()
        {
            File.WriteAllBytes(databasePath, JsonSerializer.SerializeToUtf8Bytes(database));
        }

        public IAsyncEnumerable<ExekiasObject> GetAllObjects() =>
            database.ToAsyncEnumerable();

        public IAsyncEnumerable<ExekiasObject> QueryDataObjects(string run, string where = "", string orderBy = "", bool orderAscending = true, int top = 0) =>
           database.Where(item => item.Run == run).ToAsyncEnumerable();

        public ValueTask<ExekiasObject?> GetMetaObject(string run)
        {
            var existing = FindIndex(run, null, ExekiasObjectType.Metadata);
            return existing < 0
                ? new ValueTask<ExekiasObject?>((ExekiasObject?)null)
                : new ValueTask<ExekiasObject?>(database[existing]);
        }

        public ValueTask<ExekiasObject?> GetDataObject(string run, string path)
        {
            var existing = FindIndex(run, path, ExekiasObjectType.Metadata);
            return existing < 0
                ? new ValueTask<ExekiasObject?>((ExekiasObject?)null)
                : new ValueTask<ExekiasObject?>(database[existing]);
        }


        public ValueTask<string?> GetRun(string path) =>
            new ValueTask<string?>(database.FirstOrDefault(item => (item.Run + '/' + item.Path) == path)?.Run);

        public ValueTask PutObject(ExekiasObject runFile)
        {
            if (null == runFile.Run || null == runFile.Path)
                throw new InvalidOperationException($"Invalid {nameof(ExekiasObject)}");
            var existing = FindIndex(runFile.Run, runFile.Path, runFile.Type);
            if (existing >= 0)
            {
                database.RemoveAt(existing);
            }
            database.Add(runFile);
            Flush();
            return new ValueTask();  // completed ValueTask
        }

        public IAsyncEnumerable<ExekiasObject> QueryMetaObjects(string where, string orderBy, bool orderAscending, int top)
        {
            throw new NotImplementedException();
        }
    }
}
