using System.CommandLine;


var rootCommand = new RootCommand("Exekias configuration");
var configOption = new Option<FileInfo>(
    new[]{"--config", "-c"},
    () => new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".exekias.json")),
    "Configuration file path.");
rootCommand.AddGlobalOption(configOption);

var runIdArgument = new Argument<string>("run", "Run identifier.");

// config -- show current configuration
var configCommand = new Command("config", "Manage local exekias configuration settings.");
configCommand.SetHandler(ctx => DoShow(ctx.ParseResult.GetValueForOption(configOption), ctx.Console));
rootCommand.AddCommand(configCommand);

// config create -- create a new configuration file.
var configCreateCommand = new Command("create", "Create a new configuration file");
var azureSubscriptionOption = new Option<string?>(new[]{"--subscription", "-s"}, "Azure subscription name or identifier.");
configCreateCommand.AddOption(azureSubscriptionOption);
var resourceGroupOption = new Option<string?>(new[]{"--resourcegroup", "-g"}, "Azure resource group name.");
configCreateCommand.AddOption(resourceGroupOption);
var storageAccountOption = new Option<string?>("--storageaccount", "Azure storage account name.");
configCreateCommand.AddOption(storageAccountOption);
var blobContainerOption = new Option<string>("--blobcontainer", "Blob container name. For a new sttorage account the default container name is 'runs'.");
configCreateCommand.AddOption(blobContainerOption);
configCreateCommand.SetHandler(ctx => DoConfigCreate(
    ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForOption(azureSubscriptionOption),
    ctx.ParseResult.GetValueForOption(resourceGroupOption),
    ctx.ParseResult.GetValueForOption(storageAccountOption),
    ctx.ParseResult.GetValueForOption(blobContainerOption),
    ctx.Console));
configCommand.AddCommand(configCreateCommand);

// runs
var runsCommand = new Command("runs", "Query and manage runs metadata.");
rootCommand.AddCommand(runsCommand);
// runs query [<query>] -- query runs that match specified criteria.
var queryCommand = new Command("query", "Query Runs that match specified criteria.");
var queryArgument = new Argument<string>("query", () => "", "The query to perform. This is the WHERE part of CosmosDB query \"SELECT * FROM run WHERE ...\". Examples: \"run.date <= '202301'\" or \"STARTSWITH(run.date, '202301')\"");
queryCommand.AddArgument(queryArgument);
var queryOrderByOption = new Option<string>("--orderby", "Order By a Run field.");
queryCommand.AddOption(queryOrderByOption);
var queryOrderAscendingOption = new Option<bool>("--orderascending", "Order Runs in ascending order. The default is descending order.");
queryCommand.AddOption(queryOrderAscendingOption);
var queryTopOption = new Option<int>("--top", () => 10, "Limit the number of results to this value.");
queryCommand.AddOption(queryTopOption);
var queryJsonOption = new Option<bool>("--json", "Output results in JSON format.");
queryCommand.AddOption(queryJsonOption);
var queryHiddenOption = new Option<bool>("--hidden", "Return hidden runs so that they can be unhidden.");
queryCommand.AddOption(queryHiddenOption);
queryCommand.SetHandler(async ctx => ctx.ExitCode = await DoQuery(
    ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForArgument(queryArgument),
    ctx.ParseResult.GetValueForOption(queryOrderByOption) ?? "lastWriteTime",
    ctx.ParseResult.GetValueForOption(queryOrderAscendingOption),
    ctx.ParseResult.GetValueForOption(queryTopOption),
    ctx.ParseResult.GetValueForOption(queryJsonOption),
    ctx.ParseResult.GetValueForOption(queryHiddenOption),
    ctx.Console));
runsCommand.AddCommand(queryCommand);
// runs show <run>
var runsShowCommand = new Command("show", "Show metadata for the run.");
runsCommand.AddCommand(runsShowCommand);
runsShowCommand.AddArgument(runIdArgument);
runsShowCommand.SetHandler(async ctx => ctx.ExitCode = await DoShow(
    ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForArgument(runIdArgument),
    ctx.Console));
// runs hide <run> [--unhide]
var runsHideCommand = new Command("hide", "Hide a run so that it doesn't show up in serach results");
runsCommand.AddCommand(runsHideCommand);
runsHideCommand.AddArgument(runIdArgument);
var runsHideUnhideOption = new Option<bool>("--unhide", "Unhide the run instead of hiding it.");
runsHideCommand.AddOption(runsHideUnhideOption);
runsHideCommand.SetHandler(async ctx => ctx.ExitCode = await DoHide(
    ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForArgument(runIdArgument),
    ctx.ParseResult.GetValueForOption(runsHideUnhideOption),
    ctx.Console));

// data
var dataCommand = new Command("data", "Manage data files.");
rootCommand.AddCommand(dataCommand);

// data ls <run> -- list data files in a run.
var dataLsCommand = new Command("ls", "List data files.");
dataCommand.AddCommand(dataLsCommand);
dataLsCommand.AddArgument(runIdArgument);
dataLsCommand.SetHandler(async ctx => await DoDataLs(
    ctx,
    ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForArgument(runIdArgument),
    ctx.Console));

// data upload <path> -- upload a run in a local folder.
var dataUploadCommand = new Command("upload", "Upload a run in a local folder.");
var dataUploadPathArgument = new Argument<string>("path", "Path to a directory with files of the run. " +
    "The directory must have a metadata file and its name must match regular expression. " +
    "See MetadataFilePath configuration value as displayed by 'config' command.");
dataUploadCommand.AddArgument(dataUploadPathArgument);
dataUploadCommand.SetHandler(async ctx => await DoDataUpload(
    ctx,
    ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForArgument(dataUploadPathArgument),
    ctx.Console));
dataCommand.AddCommand(dataUploadCommand);

// data download <run> <path>
var dataDownloadCommand = new Command("download", "Download a run as a subfolder at the specified path.");
dataDownloadCommand.AddArgument(runIdArgument);
var dataDownloadPathArgument = new Argument<string>("path", "A directory path to create the subfolder.");
dataDownloadCommand.AddArgument(dataDownloadPathArgument);
dataDownloadCommand.SetHandler(async ctx => await DoDataDownload(
    ctx,
    ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForArgument(runIdArgument),
    ctx.ParseResult.GetValueForArgument(dataDownloadPathArgument),
    ctx.Console));
dataCommand.AddCommand(dataDownloadCommand);

// backend
var backendCommand = new Command("backend", "Manage backend resources.");
rootCommand.AddCommand(backendCommand);

// backend deploy -- create or update backend services for a given blob container.
var backendDeployCommand = new Command("deploy", "Create or update backend services for a given blob container.");
backendCommand.AddCommand(backendDeployCommand);
backendDeployCommand.AddOption(azureSubscriptionOption);
backendDeployCommand.AddOption(resourceGroupOption);
backendDeployCommand.AddOption(storageAccountOption);
backendDeployCommand.AddOption(blobContainerOption);
// location option
var locationOption = new Option<string>("--location", "Azure location for the resource group.");
backendDeployCommand.AddOption(locationOption);
// interactive authentication option
var interactiveAuthOption = new Option<bool>("--interactiveauth", "Use interactive authentication.");
backendDeployCommand.AddOption(interactiveAuthOption);
backendDeployCommand.SetHandler(ctx => DoBackendDeploy(
    ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForOption(interactiveAuthOption),
    ctx.ParseResult.GetValueForOption(azureSubscriptionOption),
    ctx.ParseResult.GetValueForOption(resourceGroupOption),
    ctx.ParseResult.GetValueForOption(locationOption),
    ctx.ParseResult.GetValueForOption(storageAccountOption),
    ctx.ParseResult.GetValueForOption(blobContainerOption),
    ctx.Console));

// backend batchapp upload <path> <name> <version> -- upload a batch application package.
var backendBatchAppCommand = new Command("batchapp", "Manage Batch Service applications.");
backendCommand.AddCommand(backendBatchAppCommand);
var backendBatchAppUploadCommand = new Command("upload", "Upload a Batch Service application package.");
backendBatchAppCommand.AddCommand(backendBatchAppUploadCommand);
var backendBatchAppUploadPathArgument = new Argument<string>("path", "Path to Batch Service application package (.ZIP) file.");
backendBatchAppUploadCommand.AddArgument(backendBatchAppUploadPathArgument);
var backendBatchAppUploadNameArgument = new Argument<string>("name", "Name of Batch Service application.");
backendBatchAppUploadCommand.AddArgument(backendBatchAppUploadNameArgument);
var backendBatchAppUploadVersionArgument = new Argument<string>("version", () => "1.0.0", "Version of Batch Service application.");
backendBatchAppUploadCommand.AddArgument(backendBatchAppUploadVersionArgument);
var backendBatchAccountOption = new Option<string?>("--rid", "Batch service resource id.");
backendBatchAppUploadCommand.AddOption(backendBatchAccountOption);
backendBatchAppUploadCommand.SetHandler(ctx => DoBackendBatchAppUpload(
    //ctx.ParseResult.GetValueForOption(configOption),
    ctx.ParseResult.GetValueForOption(backendBatchAccountOption),
    ctx.ParseResult.GetValueForArgument(backendBatchAppUploadPathArgument),
    ctx.ParseResult.GetValueForArgument(backendBatchAppUploadVersionArgument),
    ctx.Console));

return await rootCommand.InvokeAsync(args);

