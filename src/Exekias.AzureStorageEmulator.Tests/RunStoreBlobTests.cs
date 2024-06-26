using System;
using Xunit;
using Azure.Storage.Blobs;
using Exekias.AzureStores;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Text;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Exekias.AzureStorageEmulator.Tests
{
    [Collection("AzureStorageEmulator")]
    public class RunStoreBlobTests : Core.Tests.RunStoreTests, IDisposable
    {
        readonly string blobContainerName = Guid.NewGuid().ToString("N");
        readonly BlobContainerClient container;
        IOptions<RunStoreBlobContainer.Options> MakeOptions(string bcn = "", string mfp = "") =>
           Options.Create(new RunStoreBlobContainer.Options()
           {
               BlobContainerUrl = bcn == "" ? "§" + blobContainerName : bcn,
               MetadataFilePattern = mfp == "" ? TestPattern : mfp,
               CacheAll = false
           });
        public RunStoreBlobTests(StorageEmulatorFixture _)
        {
            store = new RunStoreBlobContainer(
                MakeOptions(),
                new Core.Tests.ImporterMock(),
                new NullLogger<RunStoreBlobContainer>(),
                null);
            container = new BlobContainerClient(StorageEmulatorFixture.ConnectionString, blobContainerName);
            container.CreateIfNotExists();
        }

        public void Dispose()
        {
            container.Delete();
        }

        protected override void AddFile(string path, string body)
        {
            container.GetBlobClient(path).Upload(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        }

        [Fact]
        public void Ctor_throws()
        {
            Assert.Throws<InvalidOperationException>(() => new RunStoreBlobContainer(
                MakeOptions(bcn: null),
                new Core.Tests.ImporterMock(),
                new NullLogger<RunStoreBlobContainer>(),
                null));
        }

        [Fact]
        public void AbsoluteBasePath_is_emulator_endpoint()
        {
            Assert.Equal(container.Uri.ToString() + '/', store.AbsoluteBasePath);
        }

        class TestException : InvalidOperationException { }
        [Fact]
        public async void GetLocalFile_deletes_temp_file_if_error()
        {
            // set up blob client to throw on download
            var blobClientMock = new Mock<BlobClient>();
            blobClientMock.Setup(c => c.DownloadAsync()).Throws<TestException>();
            var containerMock = new Mock<BlobContainerClient>();
            containerMock.Setup(c => c.GetBlobClient(It.IsAny<string>()))
                .Returns(blobClientMock.Object);
            var storeMock = new Mock<RunStoreBlobContainer>(
                MakeOptions(),
                new Core.Tests.ImporterMock(),
                new NullLogger<RunStoreBlobContainer>(),
                null);
            storeMock.CallBase = true;
            storeMock.Protected().SetupGet<BlobContainerClient>("ContainerClient").Returns(containerMock.Object);
            await Assert.ThrowsAsync<TestException>(() => storeMock.Object.GetLocalFile("anything.csv").AsTask());
        }
    }
}
