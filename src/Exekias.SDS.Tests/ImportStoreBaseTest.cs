using Exekias.Core;
using Microsoft.Research.Science.Data;
using Microsoft.Research.Science.Data.NetCDF4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Exekias.SDS.Tests
{
    public abstract class ImportStoreBaseTest
    {
        protected DataImporter importer;
        protected ImportStoreBase store;

        [Fact]
        public void CanImport_sds_files()
        {
            Assert.True(importer.CanImport("abc.csv"));
            Assert.True(importer.CanImport("def/abc.tsv"));
            //Assert.True(importer.CanImport("http://host/container/run/abc.nc"));
            //Assert.True(importer.CanImport("c:/container/run/abc.nc"));
            //Assert.True(importer.CanImport("file:///c:/container/run/abc.nc"));
        }

        [Fact]
        public void CanImport_docx_false()
        {
            Assert.False(importer.CanImport("abc.docx"));
        }

        [Fact]
        public async Task Import_csv()
        {
            // New file
            var runPath = "run/path";
            var csvPath = "subfolder/data.csv";
            var runFile = new RunFileRegex(runPath + "/" + csvPath, DateTimeOffset.Now, Match.Empty, new List<string>(0));
            var csvObject = new ExekiasObject(runPath, runFile);
            // new file
            using (var localFile = new LocalTempFile("subfolder/data.csv"))
            {
                File.WriteAllText(localFile.LocalPath, "a,b\n1,3.14\n");
                var varChanged = await store.Import(new ValueTask<LocalFile>(localFile), csvObject);
                Assert.True(varChanged);
                var csvVariables = csvObject.GetVariables()["CSV"];
                Assert.Equal(2, csvVariables.Length);
                Assert.Contains("a", csvVariables);
                Assert.Contains("b", csvVariables);
            }
            await using (var dataSetFile = await store.GetDataSetFile(csvObject))
            {
                var dsUri = new NetCDFUri()
                {
                    OpenMode = ResourceOpenMode.ReadOnly,
                    FileName = dataSetFile.LocalPath
                };
                using var ds = DataSet.Open(dsUri);
                Assert.Equal(2, ds.Variables.Count);
                Assert.Equal(1, ds.Dimensions.Count);
                Assert.Equal(1, ds.Dimensions[0].Length);
            }
            // changed file
            using (var localFile = new LocalTempFile("subfolder/data.csv"))
            {
                File.WriteAllText(localFile.LocalPath, "a,b\n2,3.14\n");
                var varChanged = await store.Import(new ValueTask<LocalFile>(localFile), csvObject);
                Assert.False(varChanged);
                var csvVariables = csvObject.GetVariables()["CSV"];
                Assert.Equal(2, csvVariables.Length);
                Assert.Contains("a", csvVariables);
                Assert.Contains("b", csvVariables);
            }
            // changed variables
            using (var localFile = new LocalTempFile("subfolder/data.csv"))
            {
                File.WriteAllText(localFile.LocalPath, "a\n3\n");
                var varChanged = await store.Import(new ValueTask<LocalFile>(localFile), csvObject);
                Assert.True(varChanged);
                var csvVariables = csvObject.GetVariables()["CSV"];
                Assert.Single(csvVariables);
                Assert.Contains("a", csvVariables);
            }


        }
    }

}
