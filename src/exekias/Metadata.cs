using Exekias.Core.Azure;
using Exekias.CosmosDb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

partial class Worker
{

    ExekiasStore CreateExekiasStore(ExekiasConfig cfg)
    {
        return new ExekiasStore(Options.Create(new ExekiasStore.Options()
        {
            Endpoint = cfg.exekiasStoreEndpoint,
            DatabaseName = cfg.exekiasStoreDatabaseName,
            ContainerName = cfg.exekiasStoreContainerName
        }), LoggerFactory.Create(builder =>
        {
            builder
                .AddFilter("Exekias.CosmosDB", LogLevel.Error)
                .AddConsole();
        }).CreateLogger<ExekiasStore>(),
        new CredentialProvider(Credential));
    }

    public async Task<int> DoQuery(
        string query,
        string orderBy,
        bool orderAscending,
        int top,
        bool jsonOutput,
        bool isHidden)
    {
        if (Config == null)
        {
            return 1;
        }
        var exekiasStore = CreateExekiasStore(Config);
        var result = await exekiasStore.QueryMetaObjects(query, orderBy.StartsWith("run.") ? orderBy.Substring(4) : orderBy, orderAscending, top, isHidden)
            .ToArrayAsync();
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions() { WriteIndented = true }));
        }
        else
        {
            foreach (var run in result)
            {
                Console.WriteLine(run.Run);
            }
        }
        //.Select(exekiasObject => JObject.Parse(System.Text.Json.JsonSerializer.Serialize(exekiasObject)))
        return 0;
    }

    public async Task<int> DoShow(string runId)
    {
        if (Config == null)
        {
            return 1;
        }
        var exekiasStore = CreateExekiasStore(Config);
        var result = await exekiasStore.GetMetaObject(runId);
        if (null == result)
        {
            WriteError($"Run '{runId}' cannot be found.");
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions() { WriteIndented = true }));
        }
        return 0;
    }

    public async Task<int> DoHide(string runId, bool unhide)
    {
        if (Config == null)
        {
            return 1;
        }
        var exekiasStore = CreateExekiasStore(Config);
        if (!await exekiasStore.SetHidden(runId, !unhide))
        {
            WriteError($"Run '{runId}' cannot be found.");
        }
        return 0;
    }
}
