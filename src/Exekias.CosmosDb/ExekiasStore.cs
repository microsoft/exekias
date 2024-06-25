using Microsoft.Azure.Cosmos;
using Exekias.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using Azure.Core;

namespace Exekias.CosmosDb
{
    public class ExekiasStore : IExekiasStore
    {
        #region Configuration
        /// <summary>
        /// Configuration options of <see cref="ExekiasStore"/> store.
        /// </summary>
        public class Options
        {
            public string? Endpoint { get; set; }
            public string DatabaseName { get; set; } = "Exekias";
            public string ContainerName { get; set; } = "Runs";
        }
        public const string OptionsSection = "ExekiasCosmos";
        readonly Options options;
        #endregion
        readonly TokenCredential credential;
        readonly ILogger logger;
        readonly Task<Container> containerPromise;
        /// <summary>
        /// Creates an instance of <see cref="IExekiasStore"/> that uses CosmosDB as a store.
        /// </summary>
        /// <param name="options">Configuration options.</param>
        /// <remarks>
        /// Typycally, obtained through dependency injection: 
        /// <c>services.Configure&lt;Exekias.CosmosDb.ExekiasStore.Options&gt;(Configuration.GetSection(Exekias.AzureStores.ExekiasCosmos.OptionsSection));</c>
        /// </remarks>
        public ExekiasStore(IOptions<Options>? options, ILogger<ExekiasStore>? logger, TokenCredential credential)
        {
            if (null == options) throw new ArgumentNullException(nameof(options));
            this.options = options.Value;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            containerPromise = InitializeContainer();
            this.credential = credential;
        }

        class SystemTextJsonSerializer : CosmosSerializer
        {
            public override T FromStream<T>(Stream stream)
            {
                var obj = JsonSerializer.Deserialize<T>(stream);
                if (obj == null) throw new ApplicationException("Cannot serialize " + typeof(T).Name);
                stream.Close();
                return obj;
            }

            public override Stream ToStream<T>(T input)
            {
                return new MemoryStream(
                    JsonSerializer.SerializeToUtf8Bytes(input));
            }
        }

        static readonly string TriggerId = "updateRunObject";

        public static Lazy<string> UpdateRunObjectTrigger = new Lazy<string>(() =>
        {
            var thisAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            var triggerResource = thisAssembly.GetName().Name + ".updateRunObject.js";
            using var triggerReader = new StreamReader(thisAssembly.GetManifestResourceStream(triggerResource));
            return triggerReader.ReadToEnd();
        });

        Task<Container> InitializeContainer()
        {
            CosmosClient dbClient = new CosmosClient(
                //options?.ConnectionString ?? throw new NullReferenceException($"ConnectionString not configured for {typeof(Options).FullName}")
                options?.Endpoint ?? throw new NullReferenceException($"Endpoint not configured for {typeof(Options).FullName}"),
                credential,
                new CosmosClientOptions() { Serializer = new SystemTextJsonSerializer() }
                );
            logger.LogInformation("CosmosDB {0}/{1} at {2}", options.DatabaseName, options.ContainerName, dbClient.Endpoint);
            return Task.FromResult(dbClient.GetContainer(options.DatabaseName, options.ContainerName));
        }

        public async ValueTask<ExekiasObject?> GetMetaObject(string runId)
        {
            var container = await containerPromise;
            try
            {
                var response = await container.ReadItemAsync<ExekiasObject>(
                    id: CosmosObject.MetaId,
                    partitionKey: new PartitionKey(runId));
                logger.LogDebug("Single object for run={0}", runId);
                return response;
            }
            catch (CosmosException error)
            {
                if (error.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.LogDebug("No object for run={0}", runId);
                    return null;
                }
                logger.LogError(error, "Error while getting an object for run={0}", runId);
                throw;
            }
        }

        public async ValueTask<ExekiasObject?> GetDataObject(string runId, string path)
        {
            var container = await containerPromise;
            try
            {
                var response = await container.ReadItemAsync<ExekiasObject>(
                    id: CosmosObject.GetId(path),
                    partitionKey: new PartitionKey(runId));
                logger.LogDebug("Single object for run={0} path={0}", runId, path);
                //return response.Value;
                return response;
            }
            catch (CosmosException error)
            {
                //if (error.Status == (int)HttpStatusCode.NotFound)
                if (error.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.LogDebug("No object for run={0} path={0}", runId, path);
                    return null;
                }
                logger.LogError(error, "Error while getting an object for run={0} path={0}", runId, path);
                throw;
            }
        }

        private async IAsyncEnumerable<T> AsyncEnumerate<T>(FeedIterator<T> iterator, string rawQuery)
        {
            if (iterator == null) yield break;
            while (iterator.HasMoreResults)
            {
                FeedResponse<T>? batch;
                try
                {
                    batch = await iterator.ReadNextAsync();
                }
                catch (CosmosException err)
                {
                    // try extract syntax errer
                    if (err.StatusCode == HttpStatusCode.BadRequest
                        && err.SubStatusCode == 0
                        && err.ResponseBody != null
                        && err.ResponseBody.StartsWith("Message: {"))
                    {
                        var matches = Regex.Match(err.ResponseBody, @"Message: ({[^\r\n]*})[\r\n]");
                        if (matches.Success)
                        {
                            // {"errors":[{"severity":"Error","location":{"start":132,"end":137},"code":"SC1001","message":"Syntax error, incorrect syntax near 'ORDER'."}]}
                            using (JsonDocument message = JsonDocument.Parse(matches.Groups[1].Value))
                            {
                                if (message.RootElement.TryGetProperty("errors", out var errors))
                                {
                                    if (errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                                    {
                                        var error = errors[0];
                                        if (error.TryGetProperty("message", out var errorMessage)
                                            && errorMessage.ValueKind == JsonValueKind.String
                                            && error.TryGetProperty("location", out var errorLocation)
                                            && errorLocation.ValueKind == JsonValueKind.Object)
                                        {
#pragma warning disable CS8604 // Possible null reference argument. errorMessage.GetString() is not null.
                                            throw new ExekiasStoreSyntaxErrorException(
                                                errorMessage.GetString(),
                                                rawQuery,
                                                (errorLocation.TryGetProperty("start", out var start) && start.TryGetInt32(out var startValue) ? startValue : 0,
                                                errorLocation.TryGetProperty("end", out var end) && end.TryGetInt32(out var endValue) ? endValue : 0));
#pragma warning restore CS8604 // Possible null reference argument.
                                        }
                                    }
                                }
                            }
                        }
                    }
                    throw;
                }
                if (batch == null) yield break;
                foreach (var item in batch)
                    yield return item;
            }
        }


        /// <inheritdoc/>
        /// <remarks>To consume bandwidth the function queries properties of required fields only.</remarks>
        public async IAsyncEnumerable<ExekiasObject> GetAllObjects()
        {
            logger.LogDebug("Start enumerating all objects in Exekias store.");
            var container = await containerPromise;
            var queryText = "SELECT r.run, r.path, r.lastWriteTime, r.id FROM r";
            var response = container.GetItemQueryIterator<CosmosObject>(queryText);
            //await foreach (var runObject in response)
            await foreach (var runObject in AsyncEnumerate(response, queryText))
                yield return runObject;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ExekiasObject> QueryDataObjects(string run, string where = "", string orderBy = "", bool orderAscending = true, int top = 0)
        {
            logger.LogDebug("Query data objects for {0} where '{1}' top {2}.", run, where, top);
            var container = await containerPromise;
            var countInfo = top <= 0 ? "" : $" TOP {top}";
            var ending = string.IsNullOrWhiteSpace(where) ? "" : $" AND ({where})"
                + (string.IsNullOrWhiteSpace(orderBy) ? "" : $" ORDER BY file[\"{orderBy}\"] "
                    + (orderAscending ? "ASC" : "DESC"));
            var query = new QueryDefinition($"SELECT{countInfo} * FROM file WHERE file.run=@runId" + ending).WithParameter("@runId", run);
            var options = new QueryRequestOptions() { PartitionKey = new PartitionKey(run) };
            var response = container.GetItemQueryIterator<CosmosObject>(query, requestOptions: options);
            //await foreach (var runObject in response)
            await foreach (var runObject in AsyncEnumerate(response, query.QueryText))
                yield return runObject;
        }

        /// <inheritdoc/>
        public async ValueTask PutObject(ExekiasObject runFile)
        {
            var container = await containerPromise;
            var cosmosObject = new CosmosObject(runFile);
            try
            {
                var options = new ItemRequestOptions();
                if (runFile.Type == ExekiasObjectType.Data)
                {
                    options.PostTriggers = new string[] { TriggerId };
                }
                await container.UpsertItemAsync(cosmosObject, new PartitionKey(runFile.Run), options);
                logger.LogDebug("Put {2} object {0} {1}", cosmosObject.Run, cosmosObject.Path, cosmosObject.Type);
            }
            catch (CosmosException error)
            {
                var json = JsonSerializer.Serialize(cosmosObject, new JsonSerializerOptions() { WriteIndented = true });
                logger.LogError(error, "Failure in PutObject\n{0}", json);
            }
        }

        /// <inheritdoc/>
        /// <remarks>The <paramref name="where"/> filter expression is a scalar expression of
        /// Azure Cosmos DB SQL query () against input_alias named 'run',
        /// e.g. STARTSWITH(run.params.config, 'alpha-') chooses runs where metadata file contents has a value 'config' starting with 'alpha-'.</remarks>
        public async IAsyncEnumerable<ExekiasObject> QueryMetaObjects(string where, string orderBy, bool orderAscending, int top, bool isHidden = false)
        {
            logger.LogDebug("Query top {0} meta objects where '{1}' order by '{2}' asc={3}.", top, where, orderBy, orderAscending);
            var container = await containerPromise;
            var builder = new System.Text.StringBuilder("SELECT");
            if (top > 0) builder.Append(" TOP ").Append(top);
            builder.Append(" * FROM run WHERE run.id = \"$\" AND");
            if (!isHidden) builder.Append(" NOT");
            builder.Append(" (IS_DEFINED(run.hidden) AND run.hidden)");
            if (!string.IsNullOrWhiteSpace(where)) builder.Append(" AND (").Append(where).Append(")");
            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                builder.Append(" ORDER BY run[\"").Append(orderBy).Append("\"] ")
                    .Append(orderAscending ? "ASC" : "DESC");
            }
            var query = builder.ToString();
            logger.LogDebug("Execute {query}", query);
            var response = container.GetItemQueryIterator<CosmosObject>(new QueryDefinition(query));
            //await foreach (var runObject in response)
            await foreach (var runObject in AsyncEnumerate(response, query))
                yield return runObject;
        }

        private static IReadOnlyList<PatchOperation> operationHiddenSet = new PatchOperation[] { PatchOperation.Set("/hidden", true) };
        private static IReadOnlyList<PatchOperation> operationHiddenUnset = new PatchOperation[] { PatchOperation.Remove("/hidden") };
        /// <inheritdoc/>
        public async ValueTask<bool> SetHidden(string runId, bool isHidden)
        {

            var container = await containerPromise;
            try
            {
                await container.PatchItemAsync<object>("$", new PartitionKey(runId),
                    isHidden ? operationHiddenSet : operationHiddenUnset,
                    new PatchItemRequestOptions() { EnableContentResponseOnWrite = false });
            }
            catch (CosmosException err)
            {
                if (err.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }
                if (!isHidden && err.StatusCode == HttpStatusCode.BadRequest && err.Message.Contains("is absent"))
                {
                    // removing "hidden" property when it doesn't exist results in this CosmosDB error; ignore it.
                    return true;
                }
                throw;
            }
            return true;
        }
    }

    /// <summary>
    /// The object to insert in the database.
    /// </summary>
    /// <remarks>
    /// CosmosDB items always have property 'id' (lower case) which must be unique within a logical partition.
    /// See https://docs.microsoft.com/en-us/azure/cosmos-db/databases-containers-items#properties-of-an-item.
    /// </remarks>
    class CosmosObject : ExekiasObject
    {
        public const string MetaId = "$"; // Id property value for object type Meta
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        public CosmosObject() { } // enabling deserialization
        public CosmosObject(ExekiasObject copyFrom)
        {
            Run = copyFrom.Run;
            Path = copyFrom.Path;
            LastWriteTime = copyFrom.LastWriteTime;
            Type = copyFrom.Type;
            Meta = copyFrom.Meta;
        }
        [JsonIgnore]
        public override ExekiasObjectType Type
        {
            get => MetaId == Id ? ExekiasObjectType.Metadata : ExekiasObjectType.Data;
            set
            {
                if (ExekiasObjectType.Metadata == value) { Id = MetaId; }
                else { Id = GetId(Path ?? ""); }
            }
        }
        [JsonPropertyName("path")]
        public override string? Path
        {
            get => base.Path;
            set
            {
                base.Path = value;
                if (ExekiasObjectType.Data == Type)
                {
                    Id = GetId(value ?? "");
                }
            }
        }
        // The following characters are restricted and cannot be used in the Id property: '/', '\\', '?', '#'
        // https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.documents.resource.id
        public static string GetId(string path) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path)).Replace('/', '-');
    }
}
