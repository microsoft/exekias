using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using Xunit;

namespace Exekias.SDS.Tests
{

    public sealed class TemporaryLocalStoreFixture : IDisposable
    {
        public string LocalPath { get; }
        public TemporaryLocalStoreFixture()
        {
            LocalPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(LocalPath);
        }
        public void Dispose()
        {
            Directory.Delete(LocalPath, true);
        }
    }

    public class ImportStoreLocalTest 
        : ImportStoreBaseTest, IClassFixture<TemporaryLocalStoreFixture>
    {
        public ImportStoreLocalTest(TemporaryLocalStoreFixture fixture)
        {
            importer = new DataImporter(new Exekias.DataImporterPart.CSV());
            store = new ImportStoreLocal(Options.Create(new ImportStoreLocal.Options()
            {
                RootDirectory = fixture.LocalPath
            }), importer, new NullLogger<ImportStoreLocal>());
        }
    }
}
