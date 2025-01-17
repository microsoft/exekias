using Azure;
using Azure.Core;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.EventGrid;
using Azure.ResourceManager.EventGrid.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using System.Text.Json;


partial class Worker
{


    public int DoShow()
    {
        if (ConfigDoesNotExist)
        {
            return 1;
        }
        WriteLine($"Configuration file {ConfigFile.FullName}");
        var runStoreResourceId = ResourceIdentifier.Parse(this.runStoreResourceId);
        SubscriptionResource subscription = Arm.GetSubscriptionResource(ResourceIdentifier.Parse(
                $"/subscriptions/{runStoreResourceId.SubscriptionId}"
            )).Get();
        WriteLine($"Subscription: {subscription.Data.SubscriptionId} ({subscription.Data.DisplayName})");
        ResourceGroupResource resourceGroup = subscription.GetResourceGroup(runStoreResourceId.ResourceGroupName);
        WriteLine($"Resource group: {resourceGroup.Data.Name} ({resourceGroup.Data.Location})");
        WriteLine($"Blob container: {Config.runStoreUrl}");
        WriteLine($"Blob metadata file pattern: {Config.runStoreMetadataFilePattern}");
        return 0;
    }

    SubscriptionResource AskSubscription(string? subscription)
    {
        var subscriptionCollection = Arm.GetSubscriptions();
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

            WriteLine($"Using subscription {all[0].Data.DisplayName} ({all[0].Data.SubscriptionId})");
            return all[0];
        }
        if (IsInputRedirected)
        {
            throw new InvalidOperationException("Specify Azure subscription using --subscription option.");
        }
        var chosen = Choose("Subscriptions:", Array.ConvertAll(all, s => s.Data.DisplayName), false);
        return all[chosen];
    }

    ResourceGroupResource AskResourceGroup(string? resourceGroupName, SubscriptionResource subscription, bool createIfNotExists, string? location)
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
            WriteLine($"Using resource group {all[0].Data.Name}");
        }
        if (IsInputRedirected)
        {
            throw new InvalidOperationException("Specify Azure resource group name using --resourcegroup option.");
        }
        var chosen = Choose("Resource groups:", Array.ConvertAll(all, s => s.Data.Name), createIfNotExists);
        if (chosen < 0)
        {
            // find out available locations for a new group in the subscription
            var locations = subscription.GetLocations(false).ToArray();
            if (locations.Length == 0)
            {
                throw new InvalidOperationException("No locations available for the subscription");
            }
            chosen = Choose("Locations:", Array.ConvertAll(locations, l => l.RegionalDisplayName), false);
            string? name = null;
            while (name == null)
            {
                // ask for a new resource group name
                System.Console.Write("Resource group name: ");
                name = System.Console.ReadLine();
                if (name == null) { throw new EndOfStreamException(); }
                if (string.IsNullOrWhiteSpace(name) || name[name.Length - 1] == '.' || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[-\w\._\(\)]+$"))
                {
                    WriteLine("Resource group name can include alphanumeric, underscore, parentheses, hyphen, period (except at end).");
                    name = null;
                }
            }
            return resourceGroups.CreateOrUpdate(Azure.WaitUntil.Completed, name, new ResourceGroupData(locations[chosen])).Value;
        }
        return all[chosen];
    }

    StorageAccountResource CreateNewStorageAccount(string name, ResourceGroupResource resourceGroup)
    {
        WriteLine($"Creating storage account {name} in {resourceGroup.Id.Name}.");
        var account = resourceGroup.GetStorageAccounts().CreateOrUpdate(Azure.WaitUntil.Completed,
            name,
            new StorageAccountCreateOrUpdateContent(
                new StorageSku("Standard_LRS"),
                StorageKind.StorageV2,
                resourceGroup.Data.Location
                )
            {
                AllowBlobPublicAccess = false,
                AllowSharedKeyAccess = false,
                IsHnsEnabled = true
            }
            ).Value;
        // retry until BlobService available on the account
        bool succeeded = false;
        while (!succeeded)
        {
            try
            {
                account.GetBlobService().Get();
                succeeded = true;
            }
            catch (RequestFailedException e)
            {
                if (e.Status == 404)
                {
                    WriteLine("Blob service not available yet, retry in 10 s.");
                    Thread.Sleep(10_000);
                }
                else { throw; }
            }

        }
        return account;
    }

    /// <summary>
    /// Ask for an existing storage account.
    /// </summary>
    /// <param name="storageAccountName">Storage account name if known.</param>
    /// <param name="resourceGroup">Case insensitive Azure resource group name if known.</param>
    /// <param name="subscription">Case insensitive Azure subscription name or id if known</param>
    /// <returns><see cref="StorageAccountResource"/> object; data not loaded yet.</returns>
    /// <exception cref="ArgumentException">A name is specified, but the resource doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">A name is not specified and cannot be requested interactively.</exception>
    StorageAccountResource AskExistingStorageAccount(string? storageAccountName, string? resourceGroup, string? subscription)
    {
        if (storageAccountName != null)
        {
            var found = StorageAccountId(storageAccountName);
            if (found == null)
            {
                throw new ArgumentException($"Storage account {storageAccountName} not found or inaccessible.");
            }
            return Arm.GetStorageAccountResource(ResourceIdentifier.Parse(found));
        }
        var subscriptions = StorageSubscriptions();
        string subscriptionId = "";
        if (subscription != null)
        {
            var foundSubscription = Array.FindIndex(subscriptions, item => item.id == subscription || string.Compare(item.name, subscription, StringComparison.InvariantCultureIgnoreCase) == 0);
            if (foundSubscription < 0)
            {
                throw new ArgumentException($"Subscription '{subscription}' doesn't exist or has no accessible storage accounts.");
            }
            subscription = subscriptions[foundSubscription].name;
            subscriptionId = subscriptions[foundSubscription].id;
        }
        else if (subscriptions.Length == 1)
        {
            subscription = subscriptions[0].name;
            subscriptionId = subscriptions[0].id;
            WriteLine($"Using subscription {subscription} ({subscriptionId}).");
        }
        else
        {
            if (IsInputRedirected)
            {
                throw new InvalidOperationException("Specify Azure subscription using --subscription option.");
            }
            var chosenSubscription = Choose("Subscriptions:", Array.ConvertAll(subscriptions, s => s.name), false);
            subscription = subscriptions[chosenSubscription].name;
            subscriptionId = subscriptions[chosenSubscription].id;
        }

        var resourceGroups = StorageResourceGroups(subscriptionId);
        if (resourceGroup != null)
        {
            if (resourceGroups.All(item => string.Compare(item, resourceGroup, StringComparison.InvariantCultureIgnoreCase) != 0))
            {
                throw new ArgumentException($"Resource group {resourceGroup} doesn't exist or has no accessible storage accounts.");
            }
        }
        else if (resourceGroups.Length == 1)
        {
            resourceGroup = resourceGroups[0];
            WriteLine($"Using resource group {resourceGroup}.");
        }
        else
        {
            if (IsInputRedirected)
            {
                throw new InvalidOperationException("Specify resource group using --resourcegroup option.");
            }
            var chosenRG = Choose("Resource groups:", resourceGroups, false);
            resourceGroup = resourceGroups[chosenRG];
        }

        var storageAccounts = Array.ConvertAll(StorageAccountIds(subscriptionId, resourceGroup), id => ResourceIdentifier.Parse(id));
        if (storageAccounts.Length == 1)
        {
            WriteLine($"Using storage account {storageAccounts[0].Name}.");
            return Arm.GetStorageAccountResource(storageAccounts[0]);
        }
        else
        {
            var chosenSA = Choose("Storage accounts:", Array.ConvertAll(storageAccounts, a => a.Name), false);
            return Arm.GetStorageAccountResource(storageAccounts[chosenSA]);
        }
    }

    StorageAccountResource AskStorageAccount(string? storageAccountName, ResourceGroupResource resourceGroup, bool createIfNotExists)
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
        if (IsInputRedirected)
        {
            throw new InvalidOperationException("Specify Azure resource group name using --storageaccount option.");
        }
        StorageAccountResource[] all = storageAccounts.GetAll().ToArray();
        var chosen = Choose("Storage accounts:", Array.ConvertAll(all, a => a.Data.Name), createIfNotExists);
        if (chosen < 0)
        {
            WriteLine("Creating a new storage account and associated backend resources.");
            // ask for a new storage account name
            StorageAccountResource? result = null;
            while (result == null)
            {
                string? name = null;
                while (name == null)
                {
                    System.Console.Write("Storage account name: ");
                    name = System.Console.ReadLine();
                    if (name == null) { throw new EndOfStreamException(); }
                    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9]{3,24}$"))
                    {
                        WriteLine("Storage account name can include 3 to 24 lower case letters and numbers.");
                        name = null;
                    }
                }
                try
                {
                    result = CreateNewStorageAccount(name, resourceGroup);
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 409 && ex.ErrorCode == "StorageAccountAlreadyTaken")
                {
                    WriteLine("The storage account name is already taken.");
                }
            }
            return result;
        }
        return all[chosen];
    }

    string AskBlobContainerName(string? blobContainerName, StorageAccountResource storageAccount)
        => AskBlobContainerName(
            blobContainerName,
            storageAccount
                .GetBlobService()
                .GetBlobContainers()
                .AsEnumerable()
                .Select(c => c.Data.Name)
                .ToList()
            );

    string AskBlobContainerName(string? blobContainerName, IList<string> all, bool createNew = true)
    {
        if (blobContainerName != null)
        {
            if (all.Contains(blobContainerName))
            {
                return blobContainerName;
            }
            else
            {
                throw new InvalidOperationException($"{blobContainerName} does not exist.");
            }
        }
        if (IsInputRedirected)
        {
            throw new InvalidOperationException("Specify blob container name using --blobcontainer option.");
        }
        var chosen = Choose("Blob containers:", all, createNew);
        if (chosen < 0)
        {
            // ask for a new blob container name
            string? name = null;
            while (name == null)
            {
                System.Console.Write("Blob container name: ");
                name = System.Console.ReadLine();
                if (name == null) { throw new EndOfStreamException(); }
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$"))
                {
                    WriteLine("Blob container name can include 3 to 63 lower case letters, numbers and hyphens.");
                    WriteLine("The name must start and end with a letter or number.");
                    WriteLine("Two or more consecutive dash characters aren't permitted.");
                    name = null;
                }
            }
            return name;
        }
        return all[chosen];
    }

    SystemTopicEventSubscriptionResource? FindEventSubscription(ResourceIdentifier storageAccount, string? blobContainerUrl)
    {
        string subscriptionId = storageAccount.SubscriptionId ?? throw new NullReferenceException();
        string resourceGroup = storageAccount.ResourceGroupName ?? throw new NullReferenceException();
        List<SystemTopicEventSubscriptionResource?> links = SystemTopicIds(subscriptionId, resourceGroup)
            .Select(id => (SystemTopicResource)Arm.GetSystemTopicResource(ResourceIdentifier.Parse(id)).Get())
            .Where(topic => topic.Data.Source == storageAccount)
            .SelectMany(topic => topic.GetSystemTopicEventSubscriptions()
                .AsEnumerable()
                .Where(eventSubscription =>
                {
                    if (eventSubscription.Data.Destination is AzureFunctionEventSubscriptionDestination destination)
                    {
                        if (blobContainerUrl is null)
                        {
                            return true;
                        }
                        else
                        {
                            var configured = Arm
                                .GetWebSiteResource(destination.ResourceId.Parent)
                                .GetApplicationSettings().Value
                                .Properties.TryGetValue("RunStore__BlobContainerUrl", out string? cnValue);
                            return configured && cnValue == blobContainerUrl;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }))
            .ToList();
        if (links.Count < 1)
        {
            WriteError($"No Exekias event subscribers found for {blobContainerUrl ?? storageAccount}.");
            return null;
        }
        if (links.Count > 1)
        {
            WriteError($"More than one Exekias event subscribers found for {blobContainerUrl ?? storageAccount}.");
            return null;
        }
        return links[0];
    }

    AppServiceConfigurationDictionary? FindWebSiteSettings(ResourceIdentifier storageAccount, string? blobContainerUrl)
    {
        var link = FindEventSubscription(storageAccount, blobContainerUrl) ?? throw new ApplicationException();
        var destination = (AzureFunctionEventSubscriptionDestination)link.Data.Destination;
        return Arm.GetWebSiteResource(destination.ResourceId.Parent).GetApplicationSettings();
    }

    public int DoConfigCreate(
        string? subscription,
        string? resourceGroupName,
        string? storageAccountName,
        string? blobContainerName)
    {
        if (ConfigFile.Exists)
        {
            WriteError($"File {ConfigFile.FullName} already exists.");
            return 1;
        }
        try
        {
            ExekiasConfig cfg;
            StorageAccountResource storageAccount = AskExistingStorageAccount(storageAccountName, resourceGroupName, subscription).Get();
            var tagPrefix = "hidden-exekias-pattern-";
            // build containerName => metadataFilePattern map
            var taggedConfigs = storageAccount.Data.Tags
                .Where(kv => kv.Key.StartsWith(tagPrefix))
                .ToDictionary(kv => kv.Key.Substring(tagPrefix.Length), kv => kv.Value);
            if (taggedConfigs.Count > 0)
            {
                if (blobContainerName != null && !taggedConfigs.ContainsKey(blobContainerName))
                {
                    WriteError($"No Exekias for {storageAccountName}/{blobContainerName}. Existing configurations: {string.Join(", ", taggedConfigs.Keys)}");
                    return 1;
                }
                if (blobContainerName == null)
                {
                    if (taggedConfigs.Count == 1)
                    {
                        blobContainerName = taggedConfigs.Keys.First();
                    }
                    else
                    {
                        blobContainerName = AskBlobContainerName(null, taggedConfigs.Keys.ToList(), false);
                    }
                }
                var cosmosUrl = storageAccount.Data.Tags.TryGetValue($"hidden-exekias-cosmos-{blobContainerName}", out string? value) ? value : null;
                if (cosmosUrl == null)
                {
                    WriteError($"No Exekias Cosmos configuration for {storageAccountName}/{blobContainerName}.");
                    return 1;
                }
                var urlParts = cosmosUrl.Split('/');
                var cosmosContainer = urlParts[^1];
                var cosmosDb = urlParts[^2];
                var cosmosEndpoint = string.Join('/', urlParts[..^2]) + "/";
                cfg = new ExekiasConfig(
                    runStoreUrl: storageAccount.Data.PrimaryEndpoints.BlobUri.ToString() + blobContainerName,
                    runStoreMetadataFilePattern: taggedConfigs[blobContainerName],
                    exekiasStoreEndpoint: cosmosEndpoint,
                    exekiasStoreDatabaseName: cosmosDb,
                    exekiasStoreContainerName: cosmosContainer
                    );
            }
            else
            {
                // For compatibility with previous versions, 
                // find a function app and read configuration from its settings
                var appSettings = FindWebSiteSettings(storageAccount.Id, storageAccount.Data.PrimaryEndpoints.BlobUri + blobContainerName);
                if (appSettings == null)
                {
                    return 1;
                }
                cfg = new ExekiasConfig(
                    runStoreUrl: appSettings.Properties["RunStore__BlobContainerUrl"],
                    runStoreMetadataFilePattern: appSettings.Properties["RunStore__MetadataFilePattern"],
                    exekiasStoreEndpoint: appSettings.Properties["ExekiasCosmos__Endpoint"],
                    exekiasStoreDatabaseName: appSettings.Properties.TryGetValue("ExekiasCosmos__DatabaseName", out string? dnValue) && dnValue != null ? dnValue : "Exekias",
                    exekiasStoreContainerName: appSettings.Properties.TryGetValue("ExekiasCosmos__ContainerName", out string? cnValue) && cnValue != null ? cnValue : "Runs"
                    );
            }
            using var file = ConfigFile.OpenWrite();
            JsonSerializer.Serialize(file, cfg);
            WriteLine($"Configuration saved in {ConfigFile.FullName}.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }


}
