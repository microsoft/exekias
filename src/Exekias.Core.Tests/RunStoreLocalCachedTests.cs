using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.IO;

namespace Exekias.Core.Tests
{
    public class RunStoreLocalCachedTests : RunStoreTests, IDisposable
    {
        readonly WorkDirFixture workDir;
        public RunStoreLocalCachedTests()
        {
            workDir = new WorkDirFixture();
            store = new RunStoreLocal(
                Options.Create(new RunStoreLocal.Options()
                {
                    RootDirectory = workDir.LocalPath,
                    MetadataFilePattern = TestPattern,
                    CacheAll = true
                }),
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
    }
}
