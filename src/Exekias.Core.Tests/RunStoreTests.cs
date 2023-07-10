using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Exekias.Core.Tests
{
    public abstract class RunStoreTests
    {
        protected static readonly string TestPattern = $"^(?<{RunStoreLocal.Options.runPathCaptureName}>(?<stamp>\\d+)?.*)/params.json$";
        protected IRunStore store;
        protected abstract void AddFile(string path, string body);

        [Fact]
        public async void AllFiles_empty()
        {
            var allFiles = await store.GetAllFiles();
            Assert.Empty(allFiles);
        }


        [Fact]
        public async void AllFiles_traverses_subdirectories()
        {
            AddFile("a.data", "body");
            AddFile("b/c.data", ""); // zero length non-importable, shoud skip
            AddFile("b/c.docx", "body"); // non-importable, shoud skip
            Assert.Matches(TestPattern, "b/params.json");
            AddFile("b/params.json", "body");
            AddFile("b/d/e.data", "body");
            var allFiles = await store.GetAllFiles();
            Assert.Equal(3, allFiles.Count());
            Assert.NotNull(allFiles.FirstOrDefault(f => f.Path == "a.data"));
            Assert.NotNull(allFiles.FirstOrDefault(f => f.Path == "b/params.json"));
            Assert.NotNull(allFiles.FirstOrDefault(f => f.Path == "b/d/e.data"));
            Assert.Equal(3, (await store.GetAllFiles()).Count());
        }

        [Fact]
        public async void AllRuns_empty()
        {
            var allRuns = await store.GetAllRuns();
            Assert.Empty(allRuns);
        }

        [Fact]
        public async void AllRuns_traverse_subdirectories()
        {
            AddFile("a/b/params.json", "body");
            AddFile("b/params.json", "body");
            AddFile("b/d/e.data", "body");
            AddFile("b/d/params.json", "body");
            Assert.Equal(4, (await store.GetAllFiles()).Count());
            var allRuns = await store.GetAllRuns();
            Assert.Equal(2, allRuns.Count);
            Assert.True(allRuns.ContainsKey("a/b"));
            Assert.True(allRuns.ContainsKey("b"));
            Assert.Equal("a/b/params.json", allRuns["a/b"].Meta.Path);
            Assert.Empty(allRuns["a/b"].Data);
            Assert.Equal("b/params.json", allRuns["b"].Meta.Path);
            Assert.Single(allRuns["b"].Data);
            Assert.Equal("b/d/e.data", allRuns["b"].Data[0].Path);
        }

        [Fact]
        public async void DataFilesUnder_empty()
        {
            var files = await store.GetDataFilesUnder("").ToListAsync();
            Assert.Empty(files);
        }

        [Fact]
        public void DataFilesUnder_null_exception()
        {
            Assert.Throws<ArgumentNullException>(() => store.GetDataFilesUnder(null));
        }

        [Fact]
        public async void DataFilesUnder_subset()
        {
            AddFile("a.data", "body");
            AddFile("b/a/c.data", "body");
            AddFile("b/params.json", "body"); // non-data skipped
            AddFile("b/d.data", ""); // empty - skipped
            AddFile("e/f.data", "body");
            Assert.Equal(4, (await store.GetAllFiles()).Count());
            var files = await store.GetDataFilesUnder("").ToListAsync();
            Assert.Equal(3, files.Count);
            files = await store.GetDataFilesUnder("e").ToListAsync();
            Assert.Single(files);
            files = await store.GetDataFilesUnder("b").ToListAsync();
            Assert.Single(files);
            files = await store.GetDataFilesUnder("a").ToListAsync();
            Assert.Empty(files);

        }

        [Fact]
        public void CreateRunFile_data()
        {
            var t = DateTimeOffset.Now;
            var observed = store.CreateRunFile("a.data", t);
            Assert.NotNull(observed);
            Assert.Equal("a.data", observed.Path);
            Assert.Equal(t, observed.LastWriteTime);
            Assert.False(observed.IsMetadataFile());
            Assert.Throws<InvalidOperationException>(() => observed.RunPath());
            Assert.Throws<InvalidOperationException>(() => observed.PathMetadata());
        }

        [Fact]
        public void CreateRunFile_meta()
        {
            var t = DateTimeOffset.Now;
            var observed = store.CreateRunFile("b/params.json", t);
            Assert.NotNull(observed);
            Assert.Equal("b/params.json", observed.Path);
            Assert.Equal(t, observed.LastWriteTime);
            Assert.True(observed.IsMetadataFile());
            Assert.Equal("b", observed.RunPath());
            Assert.Empty(observed.PathMetadata());
            observed = store.CreateRunFile("000_b/c/params.json", t);
            Assert.True(observed.IsMetadataFile());
            Assert.Equal("000_b/c", observed.RunPath());
            Assert.Single(observed.PathMetadata());
            Assert.Equal("000", observed.PathMetadata()["stamp"]);
        }
        [Fact]
        public void CreateRunFile_throws()
        {
            Assert.Throws<ArgumentNullException>(() => store.CreateRunFile(null, DateTimeOffset.MinValue));
        }

        [Fact]
        public async void LocalFile()
        {
            AddFile("b/a/c.data", "body");
            var observed = await store.GetLocalFile("b/a/c.data");
            try
            {
                Assert.NotNull(observed);
                Assert.True(File.Exists(observed.LocalPath));
                Assert.Equal("body", File.ReadAllText(observed.LocalPath));
            }
            finally
            {
                await observed.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

