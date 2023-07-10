using Exekias.CosmosDb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;

partial class Program
{

    static async Task DoQuery(
        InvocationContext ctx,
        FileInfo? cfgFile,
        string query,
        string orderBy,
        bool orderAscending,
        int top,
        bool jsonOutput,
        IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console);
        if (cfg == null)
        {
            ctx.ExitCode = 1;
            return;
        }
        var exekiasStore = new ExekiasStore(Options.Create(new ExekiasStore.Options()
        {
            ConnectionString = cfg.exekiasStoreConnectionString,
            DatabaseName = cfg.exekiasStoreDatabaseName,
            ContainerName = cfg.exekiasStoreContainerName
        }), LoggerFactory.Create(builder =>
        {
            builder
                .AddFilter("Exekias.CosmosDB", LogLevel.Error)
                .AddConsole();
        }).CreateLogger<ExekiasStore>());
        var result = await exekiasStore.QueryMetaObjects(query, orderBy.StartsWith("run.") ? orderBy.Substring(4) : orderBy, orderAscending, top)
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
        return;
    }
}