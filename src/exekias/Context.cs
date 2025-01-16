using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

public record ExekiasConfig(
    [property: JsonRequired] string runStoreUrl,
    [property: JsonRequired] string runStoreMetadataFilePattern,
    [property: JsonRequired] string exekiasStoreEndpoint,
    [property: JsonRequired] string exekiasStoreDatabaseName,
    [property: JsonRequired] string exekiasStoreContainerName);

public class Context(InvocationContext cmdContext)
{
    #region implemetation

    static ArmClient? _arm = null;

    TokenCredential GetCredential()
    {
        var option = cmdContext.ParseResult.GetValueForOption<string>(credentialOption);
        return option switch
        {
            null => new DefaultAzureCredential(
                new DefaultAzureCredentialOptions()
                {
                    ExcludeInteractiveBrowserCredential = false,
                    ExcludeManagedIdentityCredential = true,
                    ExcludeWorkloadIdentityCredential = true
                }),
            "interactive" => new InteractiveBrowserCredential(),
            "cli" => new AzureCliCredential(),
            "pwsh" => new AzurePowerShellCredential(),
            "msi" => new ManagedIdentityCredential(),
            "devicecode" => new DeviceCodeCredential(),
            _ => new ManagedIdentityCredential(option)
        };
    }



    ArmClient GetArmClient()
    {
        if (_arm is not null) return _arm;
        _arm = new(GetCredential());
        return _arm;
    }

    string resourceId(string query, string key)
    {
        var tenant = GetArmClient().GetTenants().First();
        ResourceQueryResult result = tenant.GetResources(new ResourceQueryContent(query));
        var errorMessage = $"Cannot find ARM resource for {key}, you may not have access to an appropriate Azure subscription/resource group.";
        if (result.TotalRecords != 1) throw new InvalidOperationException(errorMessage);
        return JsonNode.Parse(result.Data)?[0]?["id"]?.GetValue<string>() ?? throw new InvalidOperationException(errorMessage);
    }
    FileInfo? _configFile = null;
    FileInfo GetConfigFile()
    {
        if (_configFile is not null) return _configFile;
        _configFile = cmdContext.ParseResult.GetValueForOption<FileInfo>(configOption);
        return _configFile ?? throw new InvalidOperationException("Config file assertion.");
    }

    ExekiasConfig? _config = null;
    bool _configLoaded = false;

    ExekiasConfig? GetConfig()
    {
        if (_configLoaded) return _config;
        var cfgFile = GetConfigFile();
        if (cfgFile.Exists)
        {
            using var file = cfgFile.OpenRead();
            try
            {
                _config = JsonSerializer.Deserialize<ExekiasConfig>(file);
            }
            catch (JsonException)
            {
                WriteError($"File {cfgFile.FullName} has invalid format.");
            }
        }
        _configLoaded = true;
        return _config;
    }

    string runStoreUrl => GetConfig()?.runStoreUrl ?? throw new InvalidOperationException("No config.");
    string exekiasStoreUrl => GetConfig()?.exekiasStoreEndpoint ?? throw new InvalidOperationException("No config.");

    #endregion
    public static Option<FileInfo> configOption = new(
        ["--config", "-c"],
        () => new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".exekias.json")),
        "Configuration file path.");

    public static Option<string> credentialOption = new(
        "--credential",
        "Credential to use, one of 'interactive', 'devicecode', 'cli', 'pwsh', 'msi' or a GUID of a user assigned managed identity.");

    public TokenCredential Credential => GetCredential();

    public ArmClient Arm => GetArmClient();

    public string runStoreResourceId => resourceId(
                $"Resources | where type =~ 'Microsoft.Storage/storageAccounts' and '{runStoreUrl}' startswith properties.primaryEndpoints.blob",
                runStoreUrl);

    public string exekiasStoreResourceId => resourceId(
                $"Resources | where type =~ 'Microsoft.DocumentDB/databaseAccounts' and properties.documentEndpoint == '{exekiasStoreUrl}'",
                exekiasStoreUrl);

    public string runStoreContainerName => new Uri(runStoreUrl).LocalPath.Substring(1);

    public bool IsInputRedirected => cmdContext.Console.IsInputRedirected;

    public void WriteLine(string message)
    {
        lock (cmdContext.Console)
        {
            cmdContext.Console.WriteLine(message);
        }
    }

    public void WriteError(string message)
    {
        var savedColor = Console.ForegroundColor;
        if (!Console.IsErrorRedirected) Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        if (!Console.IsErrorRedirected) Console.ForegroundColor = savedColor;
    }

    /// <summary>
    /// Display a list to choose from and prompt for a choice.
    /// </summary>
    /// <param name="title">Title of the list.</param>
    /// <param name="items">Items to choose from.</param>
    /// <param name="createNew">Add (create new) item on top.</param>
    /// <returns>Index of the chosen item.</returns>
    /// <exception cref="InvalidOperationException">System console is not interactive.</exception>
    /// <exception cref="EndOfStreamException">Input has been closed without giving a choice.</exception>
    public int Choose(string title, IList<string> items, bool createNew)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("Interactive mode not available as standard input is redirected.");
        }
        int lower = createNew ? 0 : 1;
        int input = -1;
        string? line = null;
        WriteLine("");
        WriteLine(title);
        if (createNew)
            WriteLine("0 - (create new)");
        for (int i = 0; i < items.Count; i++) { WriteLine($"{i + 1} - {items[i]}"); }
        while (input < 0)
        {
            cmdContext.Console.Write($"Choose a number between {lower} and {items.Count}: ");
            line = Console.ReadLine();
            if (line == null) { throw new EndOfStreamException(); }
            if (int.TryParse(line, out int parsed) && parsed >= lower && parsed <= items.Count) { input = parsed; }
        }
        return input - 1;
    }

    public ProgressIndicator CreateProgressIndicator()
    {
        return new ProgressIndicator(cmdContext.Console);
    }

    public ExekiasConfig Config => GetConfig() ?? throw new ArgumentNullException("No configuration file found.");
    public ExekiasConfig? ConfigOrNone => GetConfig();
    public FileInfo ConfigFile => GetConfigFile();
    public bool ConfigDoesNotExist
    {
        get
        {
            if (Config is null)
            {
                WriteError("No configuration file found.");
                return true;
            }
            return false;
        }
    }
    public (string name, string id)[] StorageSubscriptions()
    {
        var tenant = GetArmClient().GetTenants().First();
        ResourceQueryResult result = tenant.GetResources(new ResourceQueryContent(
            $"resources | where type=='microsoft.storage/storageaccounts' | join (resourcecontainers | where type=='microsoft.resources/subscriptions' | project subscriptionId, sname=name) on subscriptionId| project id=subscriptionId, name=sname"
            ));
        return JsonNode.Parse(result.Data)?.AsArray()?.Select(item => (item?["name"]?.GetValue<string>() ?? "(null)", item?["id"]?.GetValue<string>() ?? "(null)")).ToArray() ?? throw new InvalidOperationException("No subscriptions with storage accounts found.");
    }

    public string[] StorageResourceGroups(string subscriptionId)
    {
        var tenant = GetArmClient().GetTenants().First();
        ResourceQueryResult result = tenant.GetResources(new ResourceQueryContent(
            $"resources|where type=='microsoft.storage/storageaccounts' and subscriptionId=='{subscriptionId}'| project resourceGroup | distinct resourceGroup"
            ));
        return JsonNode.Parse(result.Data)?.AsArray()?.Select(item => item?["resourceGroup"]?.GetValue<string>() ?? "(null)").ToArray() ?? throw new InvalidOperationException("No resource groups with storage accounts found.");
    }

    public string[] StorageAccountIds(string subscriptionId, string resourceGroup)
    {
        var tenant = GetArmClient().GetTenants().First();
        var query = $"resources|where type=='microsoft.storage/storageaccounts' and subscriptionId=='{subscriptionId}' and resourceGroup=='{resourceGroup}' | project id";
        ResourceQueryResult result = tenant.GetResources(new ResourceQueryContent(query));
        return JsonNode.Parse(result.Data)?.AsArray()?.Select(item => item?["id"]?.GetValue<string>() ?? "(null)").ToArray() ?? throw new InvalidOperationException("No storage accounts found.");
    }

    public string? StorageAccountId(string storageAccountName)
    {
        var tenant = GetArmClient().GetTenants().First();
        var query = $"resources|where type=='microsoft.storage/storageaccounts' and name=='{storageAccountName}'|project id ";
        ResourceQueryResult result = tenant.GetResources(new ResourceQueryContent(query));
        var found = JsonNode.Parse(result.Data)?.AsArray();
        return found?.Count == 1 ? found[0]?["id"]?.GetValue<string>() : null;
    }


    public string[] SystemTopicIds(string subscriptionId, string resourceGroup)
    {
        var tenant = GetArmClient().GetTenants().First();
        var query = $"resources|where type=='microsoft.eventgrid/systemtopics' and subscriptionId=='{subscriptionId}' and resourceGroup=='{resourceGroup}' | project id";
        ResourceQueryResult result = tenant.GetResources(new ResourceQueryContent(query));
        return JsonNode.Parse(result.Data)?.AsArray()?.Select(item => item?["id"]?.GetValue<string>() ?? "(null)").ToArray() ?? throw new InvalidOperationException("No storage accounts found.");
    }
}

public partial class Worker : Context
{
    public Worker(InvocationContext cmdContext) : base(cmdContext) { }
}