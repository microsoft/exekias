using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Exekias.Core;

partial class Worker
{
    BlobContainerClient CreateBlobContainerClient()
    {
        return new BlobContainerClient(new Uri(Config.runStoreUrl), Credential);
    }

    public async Task<int> DoDataLs(string run)
    {
        if (ConfigDoesNotExist)
        {
            return 1;
        }
        var containerClient = CreateBlobContainerClient();
        var prefix = run == "" ? "" : run + '/';
        await containerClient.GetBlobsAsync(prefix: prefix).ForEachAsync(blob =>
        {
            if (blob.Properties.ContentLength > 0)  // floders have zero length
            {
                WriteLine($"{blob.Properties.LastModified?.LocalDateTime} {blob.Properties.ContentLength,19} {blob.Name.Substring(prefix.Length)}");
            }
        });
        return 0;
    }

    // blob metadata key
    const string LAST_WRITE_TIME = "LastWriteTimeSecondsSinceEpoch";

    static DateTimeOffset BlobLastWriteTime(BlobProperties blobProperties)
    {
        if (blobProperties.Metadata.TryGetValue(LAST_WRITE_TIME, out string? value) && value != null)
        {
            var secondsSinceEpoch = double.Parse(value);
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(secondsSinceEpoch * 1000));
        }
        return blobProperties.LastModified;
    }

    public async Task<int> DoDataUpload(string path)
    {
        if (ConfigDoesNotExist)
        {
            return 1;
        }
        var dir = new DirectoryInfo(path);
        // check that path is a directory
        if (dir.Parent == null || !dir.Exists)
        {
            WriteLine($"Path {path} is not a proper directory");
            return 1;
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
        var metadataFilePattern = new System.Text.RegularExpressions.Regex(Config.runStoreMetadataFilePattern);
        if (files.Where(file => metadataFilePattern.IsMatch(file.blobName)).Count() != 1)
        {
            WriteLine($"Path {dir.FullName} does not contain a single file matching {Config.runStoreMetadataFilePattern}");
            return 1;
        }
        // create ContainerClient object
        var containerClient = CreateBlobContainerClient();
        var verbosity = VerbosityLevel;
        // enumerate all files in parallel from dir, recursively, and create upload tasks
        ProgressIndicator pi = CreateProgressIndicator();
        Task[] uploadTasks = await files.ToAsyncEnumerable().SelectAwait(async file =>
        {
            var blobClient = containerClient.GetBlobClient(file.blobName);
            // upload file only if blob doesn't exist or older
            if (await blobClient.ExistsAsync())
            {
                BlobProperties blobProperties = await blobClient.GetPropertiesAsync();
                var localFileHash = Utils.ComputeSHA256(file.info.FullName);
                var blobHash = blobProperties.Metadata.ContainsKey("sha256") ? blobProperties.Metadata["sha256"] : null;

                if ((blobProperties.ContentLength == file.info.Length)
                    && (string.Equals(blobHash, localFileHash, StringComparison.OrdinalIgnoreCase)))
                {
                    if (verbosity > Verbosity.Normal)
                    {
                        WriteLine($"Skipping {file.info.FullName} because it is up to date.");
                    }
                    pi.NewProgress(-1).Report(0);  // report skipped
                    return Task.CompletedTask;
                }
            }
            if (verbosity > Verbosity.Normal)
            {
                WriteLine($"Uploading {file.info.Length} B of {file.info.FullName}.");
            }
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
                metadata["sha256"] = Utils.ComputeSHA256(file.info.FullName);
                await blobClient.SetMetadataAsync(metadata);
            });
        }).ToArrayAsync();
        // await when all uploads complete
        await Task.WhenAll(uploadTasks);
        pi.Flush();
        return 0;
    }

    // download all data files for a run to a local path
    public async Task<int> DoDataDownload(string run, string path)
    {
        if (ConfigDoesNotExist)
        {
            return 1;
        }
        // check path exists and is a directory
        if (!Directory.Exists(path))
        {
            WriteLine($"Path {path} does not exist");
            return 1;
        }
        // check run is not empty
        if (run == "")
        {
            WriteLine($"Run id cannot be empty");
            return 1;
        }
        var verbosity = VerbosityLevel;
        var containerClient = CreateBlobContainerClient();
        var prefix = run + '/';
        var tasks = new List<Task>();
        ProgressIndicator pi = CreateProgressIndicator();
        await containerClient.GetBlobsAsync(prefix: prefix).ForEachAsync(blob =>
        {
            var blobClient = containerClient.GetBlobClient(blob.Name);
            var localPath = Path.Combine(path, blob.Name);
            var localDir = Path.GetDirectoryName(localPath) ?? throw new Exception("Unexpected algorithmic error.");
            if (!Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
            }
            if (blob.Properties.ContentLength > 0)  // floders have zero length
            {
                tasks.Add(Task.Run(async () =>
                {
                    BlobProperties blobProperties = await blobClient.GetPropertiesAsync();
                    var blobLastWriteTime = BlobLastWriteTime(blobProperties);
                    var fi = new FileInfo(localPath);
                    if (fi.Exists && fi.Length == blobProperties.ContentLength
                    && Math.Abs((blobLastWriteTime - fi.LastWriteTimeUtc).TotalMilliseconds) < 1)
                    {
                        if (verbosity > Verbosity.Normal)
                        {
                            WriteLine($"Skipping {fi.FullName} because it is up to date.");
                        }
                        pi.NewProgress(-1).Report(0);  // report skipped
                    }
                    else
                    {
                        if (verbosity > Verbosity.Normal)
                        {
                            WriteLine($"Downloading {blobProperties.ContentLength} B to {fi.FullName}.");
                        }
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
            }
        });
        await Task.WhenAll(tasks);
        pi.Flush();
        return 0;
    }
}