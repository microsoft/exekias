using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using Xunit;

namespace Exekias.Core.Tests
{
    public class RunStoreLocalTests : RunStoreTests, IDisposable
    {
        readonly WorkDirFixture workDir;
        IOptions<RunStoreLocal.Options> MakeOptions(string rd = "", string mfp = "") =>
            Options.Create(new RunStoreLocal.Options()
            {
                RootDirectory = rd == "" ? workDir.LocalPath : rd,
                MetadataFilePattern = mfp == "" ? TestPattern : mfp,
                CacheAll = false
            });
        public RunStoreLocalTests()
        {
            workDir = new WorkDirFixture();
            store = new RunStoreLocal(
                MakeOptions(),
                new ImporterMock(),
                NullLogger<RunStoreLocal>.Instance);
        }

        protected override void AddFile(string path, string body)
        {
            var fullPath = Path.Combine(workDir.LocalPath, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, body);
        }
        public void Dispose()
        {
            workDir.Dispose();
        }

        [Fact]
        public void Ctor_throws_no_options()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new RunStoreLocal(null, new ImporterMock(), NullLogger<RunStoreLocal>.Instance));
            Assert.Throws<ArgumentNullException>(() =>
                new RunStoreLocal(Options.Create<RunStoreLocal.Options>(null), new ImporterMock(), NullLogger<RunStoreLocal>.Instance));
            Assert.Throws<ArgumentNullException>(() =>
                new RunStoreLocal(MakeOptions(), null, NullLogger<RunStoreLocal>.Instance));
            Assert.Throws<ArgumentNullException>(() =>
                new RunStoreLocal(MakeOptions(), new ImporterMock(), null));
            Assert.Throws<InvalidOperationException>(() =>
                new RunStoreLocal(MakeOptions(mfp: null), new ImporterMock(), NullLogger<RunStoreLocal>.Instance));
            Assert.Throws<InvalidOperationException>(() =>
                new RunStoreLocal(MakeOptions(rd: null), new ImporterMock(), NullLogger<RunStoreLocal>.Instance));
            // RootDirectory must exist
            Assert.Throws<InvalidOperationException>(() =>
                new RunStoreLocal(MakeOptions(rd: workDir.LocalPath + "nonce"), new ImporterMock(), NullLogger<RunStoreLocal>.Instance));
            // MetadataFilePattern must contain RunId capture
            Assert.Throws<InvalidOperationException>(() =>
                new RunStoreLocal(MakeOptions(mfp: ".*/params.json"), new ImporterMock(), NullLogger<RunStoreLocal>.Instance));
        }

        [Fact]
        public void AbsoluteBasePath_is_RootDirectory()
        {
            Assert.Equal(workDir.LocalPath, store.AbsoluteBasePath);
        }
    }
}
