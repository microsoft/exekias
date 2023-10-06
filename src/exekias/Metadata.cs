﻿using Exekias.CosmosDb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;

partial class Program
{

    static async Task<int> DoQuery(
        FileInfo? cfgFile,
        string query,
        string orderBy,
        bool orderAscending,
        int top,
        bool jsonOutput,
        bool isHidden,
        IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console);
        if (cfg == null)
        {
            return 1;
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

    static async Task<int> DoShow(
        FileInfo? cfgFile,
        string runId,
        IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console);
        if (cfg == null)
        {
            return 1;
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
        var result = await exekiasStore.GetMetaObject(runId);
        if (null == result)
        {
            Error(console, $"Run '{runId}' cannot be found.");
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions() { WriteIndented = true }));
        }
        return 0;
    }

    static async Task<int> DoHide(
        FileInfo? cfgFile,
        string runId,
        bool unhide,
        IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console);
        if (cfg == null)
        {
            return 1;
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
        if (!await exekiasStore.SetHidden(runId, !unhide))
        {
            Error(console, $"Run '{runId}' cannot be found.");
        }
        return 0;
    }
}
