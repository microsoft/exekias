using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.EventGrid;
using Azure.ResourceManager.EventGrid.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using System.CommandLine;
using System.CommandLine.IO;
using System.Text.Json;

record ExekiasConfig(
    string subscriptionResourceId,
    string resourceGroup,
    string storageAccount,
    string runStoreConnectionString,
    string runStoreContainerName,
    string runStoreMetadataFilePattern,
    string exekiasStoreConnectionString,
    string exekiasStoreDatabaseName,
    string exekiasStoreContainerName);

partial class Program
{
    static TokenCredential credential = new AzurePowerShellCredential(); // Azure.Identity.DefaultAzureCredential(
        // new DefaultAzureCredentialOptions() {
        //     ExcludeInteractiveBrowserCredential = false,
        //     ExcludeManagedIdentityCredential = true,
        //     ExcludeWorkloadIdentityCredential = true
        // });
    static Lazy<ArmClient> arm = new(() => new(credential));

    static void Error(IConsole console, string message)
    {
        var savedColor = Console.ForegroundColor;
        if (!console.IsErrorRedirected) System.Console.ForegroundColor = ConsoleColor.Red;
        console.Error.WriteLine(message);
        if (!console.IsErrorRedirected) System.Console.ForegroundColor = savedColor;
    }

    static int Choose(string title, string[] items, bool createNew, IConsole console)
    {
        if (console.IsInputRedirected)
        {
            throw new InvalidOperationException("Interactive mode not available as standard input is redirected.");
        }
        int lower = createNew ? 0 : 1;
        int input = -1;
        string? line = null;
        console.WriteLine("");
        console.WriteLine(title);
        if (createNew)
            console.WriteLine("0 - (create new)");
        for (int i = 0; i < items.Length; i++) { console.WriteLine($"{i + 1} - {items[i]}"); }
        while (input < 0)
        {
            console.Write($"Choose a number between {lower} and {items.Length}: ");
            line = System.Console.ReadLine();
            if (line == null) { throw new System.IO.EndOfStreamException(); }
            if (int.TryParse(line, out int parsed) && parsed >= lower && parsed <= items.Length) { input = parsed; }
        }
        return input - 1;
    }

    static ExekiasConfig? LoadConfig(FileInfo? cfgFile, IConsole console, bool absentOk = false)
    {
        if (cfgFile == null) throw new ArgumentNullException(nameof(cfgFile));
        if (!cfgFile.Exists)
        {
            if (!absentOk)
            {
                Error(console, $"File {cfgFile.FullName} dosn't exist. To create a new configuration file use 'config create' command.");
            }
            return null;
        }
        using var file = cfgFile.OpenRead();
        var cfg = JsonSerializer.Deserialize<ExekiasConfig>(file);
        if (cfg == null)
        {
            Error(console, $"File {cfgFile.FullName} has invalid format.");
            return null;
        }
        return cfg;
    }

    static int DoShow(FileInfo? cfgFile, IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console);
        if (cfg == null) { return 1; }
        console.WriteLine($"Configuration file {cfgFile?.FullName}");
        SubscriptionResource subscription = arm.Value.GetSubscriptionResource(new ResourceIdentifier(cfg.subscriptionResourceId)).Get();
        console.WriteLine($"Subscription: {subscription.Data.SubscriptionId} ({subscription.Data.DisplayName})");
        ResourceGroupResource resourceGroup = subscription.GetResourceGroup(cfg.resourceGroup);
        console.WriteLine($"Resource group: {resourceGroup.Data.Name} ({resourceGroup.Data.Location})");
        console.WriteLine($"Blob container: {cfg.runStoreContainerName} at {cfg.storageAccount}");
        console.WriteLine($"Blob metadata file pattern: {cfg.runStoreMetadataFilePattern}");
        return 0;
    }

    static SubscriptionResource AskSubscription(string? subscription, IConsole console)
    {
        var subscriptionCollection = arm.Value.GetSubscriptions();
        // try get subscription by id
        if (subscription != null)
        {
            try
            {
                return subscriptionCollection.Get(subscription);
            }
            catch
            {
            }
        }
        SubscriptionResource[] all = subscriptionCollection.GetAll().ToArray();
        if (all.Length == 0) { throw new InvalidOperationException("No Azure subscription found."); }
        if (subscription != null)
        {
            var found = Array.FindIndex(all, s =>
                s.Data.SubscriptionId == subscription
                || string.Compare(s.Data.DisplayName, subscription, true) == 0);
            if (found >= 0)
            {
                return all[found];
            }
            else
            {
                throw new InvalidOperationException($"Cannot access subscription {subscription}");
            }
        }
        if (all.Length == 1)
        {

            console.WriteLine($"Using subscription {all[0].Data.DisplayName} ({all[0].Data.SubscriptionId})");
            return all[0];
        }
        if (console.IsInputRedirected)
        {
            throw new InvalidOperationException("Specify Azure subscription using --subscription option.");
        }
        var chosen = Choose("Subscriptions:", Array.ConvertAll(all, s => s.Data.DisplayName), false, console);
        return all[chosen];
    }

    static ResourceGroupResource AskResourceGroup(string? resourceGroupName, SubscriptionResource subscription, bool createIfNotExists, string? location, IConsole console)
    {
        ResourceGroupCollection resourceGroups = subscription.GetResourceGroups();
        if (resourceGroupName != null)
        {
            if (resourceGroups.Exists(resourceGroupName))
            {
                return resourceGroups.Get(resourceGroupName);
            }
            else
            {
                if (createIfNotExists)
                {
                    if (location == null)
                    {
                        throw new InvalidOperationException("Specify location for the new resource group using --location option.");
                    }
                    return resourceGroups.CreateOrUpdate(Azure.WaitUntil.Completed, resourceGroupName, new ResourceGroupData(location)).Value;
                }
                throw new InvalidOperationException($"{resourceGroupName} does not exist");
            }
        }
        ResourceGroupResource[] all = resourceGroups.GetAll().ToArray();
        if (all.Length == 1)
        {
            console.WriteLine($"Using resource group {all[0].Data.Name}");
        }
        if (console.IsInputRedirected)
        {
            throw new InvalidOperationException("Specify Azure resource group name using --resourcegroup option.");
        }
        var chosen = Choose("Resource groups:", Array.ConvertAll(all, s => s.Data.Name), createIfNotExists, console);
        if (chosen < 0)
        {
            // find out available locations for a new group in the subscription
            var locations = subscription.GetLocations(false).ToArray();
            if (locations.Length == 0)
            {
                throw new InvalidOperationException("No locations available for the subscription");
            }
            chosen = Choose("Locations:", Array.ConvertAll(locations, l => l.RegionalDisplayName), false, console);
            string? name = null;
            while (name == null)
            {
                // ask for a new resource group name
                console.Write("Resource group name: ");
                name = System.Console.ReadLine();
                if (name == null) { throw new EndOfStreamException(); }
                if (string.IsNullOrWhiteSpace(name) || name[name.Length - 1] == '.' || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[-\w\._\(\)]+$"))
                {
                    console.WriteLine("Resource group name can include alphanumeric, underscore, parentheses, hyphen, period (except at end).");
                    name = null;
                }
            }
            return resourceGroups.CreateOrUpdate(Azure.WaitUntil.Completed, name, new ResourceGroupData(locations[chosen])).Value;
        }
        return all[chosen];
    }

    static StorageAccountResource CreateNewStorageAccount(string name, ResourceGroupResource resourceGroup)
    {
        return resourceGroup.GetStorageAccounts().CreateOrUpdate(Azure.WaitUntil.Completed,
            name,
            new StorageAccountCreateOrUpdateContent(
                new StorageSku("Standard_LRS"),
                StorageKind.StorageV2,
                resourceGroup.Data.Location
                )
            {
                AllowBlobPublicAccess = false
            }
            ).Value;
    }

    static StorageAccountResource AskStorageAccount(string? storageAccountName, ResourceGroupResource resourceGroup, bool createIfNotExists, IConsole console)
    {
        StorageAccountCollection storageAccounts = resourceGroup.GetStorageAccounts();
        if (storageAccountName != null)
        {
            if (storageAccounts.Exists(storageAccountName))
            {
                return storageAccounts.Get(storageAccountName);
            }
            else
            {
                if (createIfNotExists)
                {
                    return CreateNewStorageAccount(storageAccountName, resourceGroup);
                }
                throw new InvalidOperationException($"{storageAccountName} does not exist in resource group {resourceGroup.Data.Name}");
            }
        }
        if (console.IsInputRedirected)
        {
            throw new InvalidOperationException("Specify Azure resource group name using --storageaccount option.");
        }
        StorageAccountResource[] all = storageAccounts.GetAll().ToArray();
        var chosen = Choose("Storage accounts:", Array.ConvertAll(all, a => a.Data.Name), createIfNotExists, console);
        if (chosen < 0)
        {
            console.WriteLine("Creating a new storage account and associated backend resources.");
            // ask for a new storage account name
            StorageAccountResource? result = null;
            while (result == null)
            {
                string? name = null;
                while (name == null)
                {
                    console.Write("Storage account name: ");
                    name = System.Console.ReadLine();
                    if (name == null) { throw new EndOfStreamException(); }
                    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9]{3,24}$"))
                    {
                        console.WriteLine("Storage account name can include 3 to 24 lower case letters and numbers.");
                        name = null;
                    }
                }
                try
                {
                    result = CreateNewStorageAccount(name, resourceGroup);
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 409 && ex.ErrorCode == "StorageAccountAlreadyTaken")
                {
                    console.WriteLine("The storage account name is already taken.");
                }
            }
            return result;
        }
        return all[chosen];
    }

    static string AskBlobContainerName(string? blobContainerName, StorageAccountResource storageAccount, IConsole console)
    {
        var all = storageAccount.GetBlobService().GetBlobContainers();
        if (blobContainerName != null)
        {
            if (all.Exists(blobContainerName))
            {
                return blobContainerName;
            }
            else
            {
                throw new InvalidOperationException($"{blobContainerName} does not exist in storage account {storageAccount.Data.Name}");
            }
        }
        if (console.IsInputRedirected)
        {
            throw new InvalidOperationException("Specify Azure resource group name using --blobcontainer option.");
        }
        var chosen = Choose("Blob containers:", Array.ConvertAll(all.GetAll().ToArray(), a => a.Data.Name), true, console);
        if (chosen < 0)
        {
            // ask for a new blob container name
            string? name = null;
            while (name == null)
            {
                console.Write("Blob container name: ");
                name = System.Console.ReadLine();
                if (name == null) { throw new EndOfStreamException(); }
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$"))
                {
                    console.WriteLine("Blob container name can include 3 to 63 lower case letters, numbers and hyphens.");
                    console.WriteLine("The name must start and end with a letter or number.");
                    console.WriteLine("Two or more consecutive dash characters aren't permitted.");
                    name = null;
                }
            }
            return name;
        }
        return all.GetAll().ToArray()[chosen].Data.Name;
    }

    static AppServiceConfigurationDictionary? FindWebSiteSettings(StorageAccountResource storageAccount, string? blobContainerName, ResourceGroupResource resourceGroup, IConsole console)
    {
        List<ResourceIdentifier?> links = resourceGroup.GetSystemTopics()
            .AsEnumerable()
            .Where(topic => topic.Data.Source == storageAccount.Id)
            .SelectMany(topic => topic.GetSystemTopicEventSubscriptions()
                .AsEnumerable()
                .Select(subscription => subscription.Data.Destination is AzureFunctionEventSubscriptionDestination destination
                    ? destination.ResourceId.Parent
                    : null)
                .Where(id =>
                {
                    if (id is null)
                    {
                        return false;
                    }
                    else if (blobContainerName is null)
                    {
                        return true;
                    }
                    else
                    {
                        var configured = arm.Value
                            .GetWebSiteResource(id)
                            .GetApplicationSettings().Value
                            .Properties.TryGetValue("RunStore:BlobContainerName", out string? cnValue);
                        return (configured && cnValue == blobContainerName) || (!configured && blobContainerName == "runs");
                    }
                }))
            .ToList();
        if (links.Count < 1)
        {
            Error(console, $"No Exekias subscribers found for the Azure Storage Account {storageAccount.Id}.");
            return null;
        }
        if (links.Count > 1)
        {
            Error(console, $"More than one Exekias subscribers found for the Azure Storage Account {storageAccount.Id}.");
            return null;
        }
        return arm.Value.GetWebSiteResource(links[0]).GetApplicationSettings();
    }

    static int DoConfigCreate(
        FileInfo? cfgFile,
        string? subscription,
        string? resourceGroupName,
        string? storageAccountName,
        string? blobContainerName,
        IConsole console)
    {
        if (cfgFile == null) throw new ArgumentNullException(nameof(cfgFile));
        if (cfgFile.Exists)
        {
            Error(console, $"File {cfgFile.FullName} already exists.");
            return 1;
        }
        try
        {
            var subscriptionResource = AskSubscription(subscription, console);
            var resourceGroup = AskResourceGroup(resourceGroupName, subscriptionResource, false, null, console);
            var storageAccount = AskStorageAccount(storageAccountName, resourceGroup, false, console);
            var appSettings = FindWebSiteSettings(storageAccount, blobContainerName, resourceGroup, console);
            if (appSettings == null)
            {
                return 1;
            }
            var cfg = new ExekiasConfig(
                subscriptionResource.Id.ToString(),
                resourceGroup.Data.Name,
                storageAccount.Data.Name,
                runStoreConnectionString: appSettings.Properties["RunStore:ConnectionString"],
                runStoreContainerName: appSettings.Properties.TryGetValue("RunStore:BlobContainerName", out string? bcnValue) && bcnValue != null ? bcnValue : "runs",
                runStoreMetadataFilePattern: appSettings.Properties["RunStore:MetadataFilePattern"],
                exekiasStoreConnectionString: appSettings.Properties["ExekiasCosmos:ConnectionString"],
                exekiasStoreDatabaseName: appSettings.Properties.TryGetValue("ExekiasCosmos:DatabaseName", out string? dnValue) && dnValue != null ? dnValue : "Exekias",
                exekiasStoreContainerName: appSettings.Properties.TryGetValue("ExekiasCosmos:ContainerName", out string? cnValue) && cnValue != null ? cnValue : "Runs"
                );
            using var file = cfgFile.OpenWrite();
            JsonSerializer.Serialize(file, cfg);
            console.WriteLine($"Configuration saved in {cfgFile.FullName}.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Error(console, ex.Message);
            return 1;
        }
    }


}
