using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using exekiascmd;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;

partial class Program
{
    static BlobContainerClient CreateBlobContainerClient(ExekiasConfig cfg)
    {
        return new BlobContainerClient(cfg.runStoreConnectionString, cfg.runStoreContainerName);
    }

    static async Task DoDataLs(
        InvocationContext ctx,
        FileInfo? cfgFile,
        string run,
        IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console);
        if (cfg == null)
        {
            ctx.ExitCode = 1;
            return;
        }
        var containerClient = CreateBlobContainerClient(cfg);
        var prefix = run == "" ? "" : run + '/';
        await containerClient.GetBlobsAsync(prefix: prefix).ForEachAsync(blob =>
        {
            console.WriteLine($"{blob.Properties.LastModified?.LocalDateTime} {blob.Properties.ContentLength,19} {blob.Name.Substring(prefix.Length)}");
        });
        return;
    }

    // blob metadata key
    const string LAST_WRITE_TIME = "LastWriteTimeSecondsSinceEpoch";

    static DateTimeOffset BlobLastWriteTime(BlobProperties blobProperties)
    {
        if (blobProperties.Metadata.TryGetValue(LAST_WRITE_TIME, out string value))
        {
            var secondsSinceEpoch = double.Parse(value);
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(secondsSinceEpoch * 1000));
        }
        return blobProperties.LastModified;
    }

    static async Task DoDataUpload(
        InvocationContext ctx,
        FileInfo? cfgFile,
        string path,
        IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console);
        if (cfg == null)
        {
            ctx.ExitCode = 1;
            return;
        }
        var dir = new DirectoryInfo(path);
        // check that path is a directory
        if (dir.Parent == null || !dir.Exists)
        {
            Console.WriteLine($"Path {path} is not a proper directory");
            ctx.ExitCode = 1;
            return;
        }
        var prefixLength = dir.Parent.FullName.Length + 1;
        // relative POSIX paths of files in the directory and its subdirectories
        var files = Array.ConvertAll(
            dir.GetFiles("*", SearchOption.AllDirectories),
            fi => (
                info: fi,
                blobName: fi.FullName.Substring(prefixLength).Replace(@"\", "/")
        ));
        // check that the directory contains a file matching regular expression runStoreMetadataFilePattern
        var metadataFilePattern = new System.Text.RegularExpressions.Regex(cfg.runStoreMetadataFilePattern);
        if (files.Where(file => metadataFilePattern.IsMatch(file.blobName)).Count() != 1)
        {
            Console.WriteLine($"Path {dir.FullName} does not contain a single file matching {cfg.runStoreMetadataFilePattern}");
            ctx.ExitCode = 1;
            return;
        }
        // create ContainerClient object
        var containerClient = CreateBlobContainerClient(cfg);
        // enumerate all files in parallel from dir, recursively, and create upload tasks
        ProgressIndicator pi = new(console);
        Task[] uploadTasks = await files.ToAsyncEnumerable().SelectAwait(async file =>
        {
            var blobClient = containerClient.GetBlobClient(file.blobName);
            // upload file only if blob doesn't exist or older
            if (await blobClient.ExistsAsync())
            {
                BlobProperties blobProperties = await blobClient.GetPropertiesAsync();
                if (blobProperties.ContentLength == file.info.Length
                    && Math.Abs((BlobLastWriteTime(blobProperties) - file.info.LastWriteTimeUtc).TotalMilliseconds) < 1)
                {
                    Trace.TraceInformation($"Skipping {file.info.FullName} because it is up to date.");
                    pi.NewProgress(-1).Report(0);  // report skipped
                    return Task.CompletedTask;
                }
            }
            Trace.TraceInformation($"Uploading {file.info.Length} B of {file.info.FullName}.");
            // upload the file content to the blob
            return Task.Run(async () =>
            {
                await blobClient.UploadAsync(file.info.FullName, new BlobUploadOptions()
                {
                    TransferOptions = new Azure.Storage.StorageTransferOptions()
                    {
                        MaximumConcurrency = 8,
                        MaximumTransferSize = 16 * 1024 * 1024,
                    },
                    ProgressHandler = pi.NewProgress(file.info.Length)
                });
                // set blob LastWriteTime metadata item to the file LastWriteTime value
                var metadata = new Dictionary<string, string>();
                metadata[LAST_WRITE_TIME] = (new DateTimeOffset(file.info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0).ToString("F3");
                await blobClient.SetMetadataAsync(metadata);
            });
        }).ToArrayAsync();
        // await when all uploads complete
        await Task.WhenAll(uploadTasks);
        pi.Flush();
        return;
    }

    // download all data files for a run to a local path
    static async Task DoDataDownload(
        InvocationContext ctx,
        FileInfo? cfgFile,
        string run,
        string path,
        IConsole console)
    {
        var cfg = LoadConfig(cfgFile, console);
        if (cfg == null)
        {
            ctx.ExitCode = 1;
            return;
        }
        // check path exists and is a directory
        if (!Directory.Exists(path))
        {
            Console.WriteLine($"Path {path} does not exist");
            ctx.ExitCode = 1;
            return;
        }
        // check run is not empty
        if (run == "")
        {
            Console.WriteLine($"Run id cannot be empty");
            ctx.ExitCode = 1;
            return;
        }
        var containerClient = CreateBlobContainerClient(cfg);
        var prefix = run + '/';
        var tasks = new List<Task>();
        ProgressIndicator pi = new(console);
        await containerClient.GetBlobsAsync(prefix: prefix).ForEachAsync(blob =>
        {
            var blobClient = containerClient.GetBlobClient(blob.Name);
            var localPath = Path.Combine(path, blob.Name);
            var localDir = Path.GetDirectoryName(localPath);
            if (localDir == null) { throw new Exception("Unexpected algorithmic error."); }
            if (!Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
            }
            tasks.Add(Task.Run(async () =>
            {
                BlobProperties blobProperties = await blobClient.GetPropertiesAsync();
                var blobLastWriteTime = BlobLastWriteTime(blobProperties);
                var fi = new FileInfo(localPath);
                if (fi.Exists && fi.Length == blobProperties.ContentLength
                    && Math.Abs((blobLastWriteTime - fi.LastWriteTimeUtc).TotalMilliseconds) < 1)
                {
                    Trace.TraceInformation($"Skipping {fi.FullName} because it is up to date.");
                    pi.NewProgress(-1).Report(0);  // report skipped
                }
                else
                {
                    Trace.TraceInformation($"Downloading {blobProperties.ContentLength} B to {fi.FullName}.");
                    await blobClient.DownloadToAsync(localPath, new BlobDownloadToOptions()
                    {
                        TransferOptions = new Azure.Storage.StorageTransferOptions()
                        {
                            MaximumConcurrency = 8,
                            MaximumTransferSize = 16 * 1024 * 1024,
                        },
                        ProgressHandler = pi.NewProgress(blobProperties.ContentLength)
                    });
                    fi.LastWriteTimeUtc = blobLastWriteTime.DateTime;
                }
            }));
        });
        await Task.WhenAll(tasks);
        pi.Flush();
        return;
    }
}