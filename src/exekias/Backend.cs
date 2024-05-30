using Azure.Core;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Batch;
using Azure.ResourceManager.Batch.Models;
using Azure.ResourceManager.EventGrid;
using Azure.ResourceManager.EventGrid.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Storage;
using Azure.Storage.Blobs.Specialized;
using System.CommandLine;
using System.CommandLine.IO;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;

partial class Program
{
    static bool SubscriptionHasRequiredProviders(SubscriptionResource subscription, IConsole console)
    {
        var providers = subscription.GetResourceProviders(); //.GetAll();
        var requiredProviders = new[] { "Microsoft.Batch", "Microsoft.EventGrid", "Microsoft.Storage", "Microsoft.Web", "Microsoft.DocumentDB", "Microsoft.Insights", "Microsoft.OperationalInsights" };
        var tasks = Array.ConvertAll(requiredProviders, async p =>
        {
            try
            {
                ResourceProviderResource providerResource = await providers.GetAsync(p);
                if (providerResource.Data.RegistrationState == "Registered") return true;
                try
                {
                    providerResource = await providerResource.RegisterAsync();
                    return providerResource.Data.RegistrationState == "Registered";
                }
                catch (Azure.RequestFailedException ex)
                {
                    if (ex.Status == 403)
                    {
                        lock (console)
                        {
                            Error(console, $"The provider {p} is not reqistered for subscription {subscription.Id.Name} and you do not have permissions to register it." +
                                " See https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-providers-and-types#register-resource-provider for details.");
                        }
                        return false;
                    }
                    throw;
                }
            }
            catch (Azure.RequestFailedException ex)
            {
                if (ex.Status == 404)
                {
                    lock (console)
                    {
                        Error(console, $"The provider {p} is not known for subscription {subscription.Id.Name}." +
                           " See https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-providers-and-types#register-resource-provider for details.");
                        return false;
                    }
                }
                throw;
            }
        });
        Task.WaitAll(tasks);
        bool result = tasks.All(t => t.Result);
        return result;
    }

    static void DeployComponents(ResourceGroupResource resourceGroup, StorageAccountResource runStore, string containerName, string deploymentName, IConsole console)
    {
        //
        // Assumes the directory contains packages 'sync.zip', 'fetch.zip' and 'tables.zip'.
        // The packages are created by running 'dotnet publish' in the corresponding project
        // and then zipping the contents of the 'bin/Debug/net6.0/publish' directory.
        // See `exekiascmd.ps1` script.
        //
        var basePath = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        if (basePath is null) { throw new InvalidOperationException("Cannot determine application base path"); }
        var templatePath = Path.Combine(basePath, "main.json");
        var syncPackagePath = Path.Combine(basePath, "Exekias.AzureFunctions.zip");
        var tablesPath = Path.Combine(basePath, "Exekias.DataImport.zip");

        var dontExist = string.Join(", ",
            (new[] { templatePath, syncPackagePath, tablesPath }).Where(p => !File.Exists(p)));
        if (dontExist.Length > 0) { throw new InvalidOperationException($"Cannot find the following file(s): {dontExist}"); }

        // Deploy ARM resources using template file
        ArmDeploymentResource deployment = resourceGroup.GetArmDeployments().CreateOrUpdate(Azure.WaitUntil.Completed,
            deploymentName,
            new ArmDeploymentContent(
                new ArmDeploymentProperties(ArmDeploymentMode.Incremental)
                {
                    Template = BinaryData.FromStream(File.OpenRead(templatePath)),
                    Parameters = BinaryData.FromObjectAsJson(new JsonObject() {
                        {"runStoreName", new JsonObject(){ {"value", runStore.Data.Name } } },
                        {"storeContainer", new JsonObject(){ {"value", containerName } } }
                    })
                })).Value;
        console.WriteLine("Deployment completed. The following resource have been created or updated:");
        foreach (var subResource in deployment.Data.Properties.OutputResources)
        {
            if (subResource is not null)
            {
                console.WriteLine($"  {subResource.Id}");
            }
        }
        var deploymentOutput = deployment.Data.Properties.Outputs.ToObjectFromJson<JsonObject>();
        var syncFunctionId = deploymentOutput["syncFunctionId"]?["value"]?.GetValue<string?>();
        var topicId = deploymentOutput["topicId"]?["value"]?.GetValue<string?>();
        var batchAccountId = deploymentOutput["batchAccountId"]?["value"]?.GetValue<string?>();
        var batchPoolId = deploymentOutput["batchPoolId"]?["value"]?.GetValue<string?>();

        // deploy syncFunction code from sync.zip
        WebSiteResource syncFunction = arm.Value.GetWebSiteResource(new ResourceIdentifier(syncFunctionId!)).Get();
        // the deployment created a new function app. Deploy code from a package.
        syncFunction = DeployFunctionCode(syncFunction, syncPackagePath);
        console.WriteLine($"Deployed package {Path.GetFileName(syncPackagePath)} to Function app {syncFunction.Id.Name}");

        // subscribe sync function app to the storage eventgrid events
        console.WriteLine("Subscribing backend to storage events.");
        SiteFunctionResource update = syncFunction.GetSiteFunctions().Get("RunChangeEventSink");
        var topic = arm.Value.GetSystemTopicResource(new ResourceIdentifier(topicId!));
        var subscriptions = topic.GetSystemTopicEventSubscriptions();
        subscriptions.CreateOrUpdate(Azure.WaitUntil.Completed, syncFunction.Data.Name, new EventGridSubscriptionData()
        {
            Destination = new AzureFunctionEventSubscriptionDestination()
            {
                ResourceId = update.Id
            }
        });

        console.WriteLine("Adding application package to batch account.");
        PoolAssignApplication(batchPoolId!,
            UploadBatchApplicationPackage(tablesPath, batchAccountId!, "dataimport", "1.0.0", console));
    }

    static WebSiteResource DeployFunctionCode(WebSiteResource funcApp, string zipPath)
    {
        // https://github.com/projectkudu/kudu/wiki/REST-API#zip-deployment
        Uri scm = funcApp.GetPublishingCredentials(Azure.WaitUntil.Completed).Value.Data.ScmUri;
        string zipDeployUri = $"https://{scm.Host}/api/zipdeploy?isAsync=true";
        using var client = new HttpClient();
        var token = credential.GetToken(new TokenRequestContext(new[] { "https://management.core.windows.net//.default" }), default);
        var request = new HttpRequestMessage(HttpMethod.Post, zipDeployUri);
        request.Headers.Add("Authorization", $"Bearer  {token.Token}");
        request.Content = new StreamContent(File.OpenRead(zipPath));
        var response = client.Send(request).EnsureSuccessStatusCode();
        // await deployment completion
        var deploymentLocation = response.Headers.Location; // https://msrcdiamond.scm.azurewebsites.net/api/deployments/latest?deployer=ZipDeploy&time=2023-06-27_07-14-46Z
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int? status = 0;
        do
        {
            Thread.Sleep(30000); // 30 sec
            request = new HttpRequestMessage(HttpMethod.Get, deploymentLocation);
            request.Headers.Add("Authorization", $"Bearer  {token.Token}");
            response = client.Send(request).EnsureSuccessStatusCode();
            var content = response.Content.ReadFromJsonAsync<JsonObject>().Result;
            status = content?["status"]?.GetValue<int>();
            if (status == 3)
            {
                throw new ApplicationException("Failed Zip deployment");
            }
        } while (status != 4 && stopwatch.Elapsed < TimeSpan.FromMinutes(5));
        return funcApp.Get();
    }

    static BatchApplicationPackageReference UploadBatchApplicationPackage(string packagePath, string batchAccountId, string appName, string version, IConsole console)
    {
        var batchAccount = arm.Value.GetBatchAccountResource(new ResourceIdentifier(batchAccountId!));
        BatchApplicationResource batchAccountApplication = batchAccount
            .GetBatchApplications()
            .CreateOrUpdate(Azure.WaitUntil.Completed, appName, new BatchApplicationData())
            .Value;
        var appPackage = batchAccountApplication
            .GetBatchApplicationPackages()
            .CreateOrUpdate(Azure.WaitUntil.Completed, version, new BatchApplicationPackageData())
            .Value;
        var blobClient = new BlockBlobClient(appPackage.Data.StorageUri);
        using var packageStream = File.OpenRead(packagePath);
        blobClient.Upload(packageStream);
        console.WriteLine($"Uploaded app package {Path.GetFileName(packagePath)} to Batch Service {batchAccount.Id.Name} as {appName} v.{version}");
        appPackage.Activate(new BatchApplicationPackageActivateContent("zip"));
        return new BatchApplicationPackageReference(batchAccountApplication.Id) { Version = version };
    }

    static void PoolAssignApplication(string batchPoolId, BatchApplicationPackageReference appReference)
    {
        var batchPool = arm.Value.GetBatchAccountPoolResource(new ResourceIdentifier(batchPoolId!));
        batchPool.Update(new BatchAccountPoolData()
        {
            ApplicationPackages = { appReference },
            ScaleSettings = new BatchAccountPoolScaleSettings()
            {
                AutoScale = new BatchAccountAutoScaleSettings(@"maxConcurrency = 10;
dormantTimeInterval = 2 * TimeInterval_Hour;
isNotDormant = $PendingTasks.GetSamplePercent(dormantTimeInterval) < 50 ? 1 : max($PendingTasks.GetSample(dormantTimeInterval));
observationTimeInterval = 1 * TimeInterval_Hour;
observedConcurrency = min(
    $PendingTasks.GetSamplePercent(observationTimeInterval) < 50 ? 1 : max(1, $PendingTasks.GetSample(observationTimeInterval)), 
    maxConcurrency);
$TargetDedicatedNodes = isNotDormant ? 1 : 0;
$TargetLowPriorityNodes = observedConcurrency - 1;
$NodeDeallocationOption = taskcompletion;")
            }
        });
    }

    static int DoBackendDeploy(
        FileInfo? cfgFile,
        bool interactiveAuth,
        string? subscription,
        string? resourceGroupName,
        string? location,
        string? storageAccountName,
        string? blobContainerName,
        IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console, absentOk: true);
        if (interactiveAuth)
        {
            credential = new Azure.Identity.InteractiveBrowserCredential();
        }
        try
        {
            var subscriptionResource = subscription == null && cfg is not null
                ? arm.Value.GetSubscriptionResource(new ResourceIdentifier(cfg.subscriptionResourceId)).Get()
                : AskSubscription(subscription, console);
            if (!SubscriptionHasRequiredProviders(subscriptionResource, console)) { return 1; }
            var resourceGroup = resourceGroupName == null && cfg is not null
                ? subscriptionResource.GetResourceGroup(cfg.resourceGroup)
                : AskResourceGroup(resourceGroupName, subscriptionResource, true, location, console);
            var storageAccount = storageAccountName == null && cfg is not null
                ? resourceGroup.GetStorageAccounts().Get(cfg.storageAccount)
                : AskStorageAccount(storageAccountName, resourceGroup, true, console);
            if (blobContainerName == null)
            {
                if (cfg is not null)
                {
                    blobContainerName = cfg.runStoreContainerName;
                }
                else if (storageAccount.GetBlobService().GetBlobContainers().Count() == 0)
                {
                    blobContainerName = "runs";
                }
                else
                {
                    blobContainerName = AskBlobContainerName(null, storageAccount, console);
                }
            }
            var deploymentName = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            console.WriteLine($"Start deployment {deploymentName} of backend services for {storageAccount.Id.Name}/{blobContainerName} in {subscriptionResource.Data.DisplayName}.");
            DeployComponents(resourceGroup, storageAccount, blobContainerName, deploymentName, console);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Error(console, ex.Message);
            return 1;
        }
    }
    static int DoBackendBatchAppUpload(
        //FileInfo? cfgFile,
        string? rid,  // temporary
        string? path,
        string? version,
        IConsole console)
    {
        try
        {
            UploadBatchApplicationPackage(path!, rid!, "dataimport", version!, console);
            return 0;
        }
        catch (Exception ex)
        {
            Error(console, $"Error {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

}
