using Azure.Storage.Blobs;
using Exekias.SDS.Blob;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using Xunit;

namespace Exekias.AzureStorageEmulator.Tests
{
    [Collection("AzureStorageEmulator")]
    public class ImportStoreBlobTest
        : SDS.Tests.ImportStoreBaseTest, IDisposable
    {
        readonly string blobContainerName = Guid.NewGuid().ToString("N");
        public ImportStoreBlobTest()
        {
            importer = new Exekias.SDS.DataImporter(new Exekias.DataImporterPart.CSV());
            store = new ImportStoreBlob(Microsoft.Extensions.Options.Options.Create(new ImportStoreBlob.Options()
            {
                BlobContainerUrl = "§" + blobContainerName
            }), importer, new NullLogger<ImportStoreBlob>(), null);
            new BlobContainerClient(StorageEmulatorFixture.ConnectionString, blobContainerName).CreateIfNotExists();
        }

        public void Dispose()
        {
            new BlobContainerClient(StorageEmulatorFixture.ConnectionString, blobContainerName).Delete();
        }
    }
}
