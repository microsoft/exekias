using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Exekias.Core.Tests
{
    public class LocalFileTests
    {
        [Fact]
        public void Can_instantiate()
        {
            using (var observed = new LocalFile("valid path/file"))
            {
                Assert.Equal("valid path/file", observed.LocalPath);
            }
        }

        [Fact]
        public void Invalid_path_not_allowed()
        {
            Assert.Throws<ArgumentException>(
                () => new LocalFile(null));
            Assert.Throws<ArgumentException>(
                () => new LocalFile(" "));
            Assert.Throws<ArgumentException>(
                () => new LocalFile("\0"));
        }

        class LocalFileAsync : LocalFile
        {
            public bool IsDisposed { get; set; } = false;
            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }
        [Fact]
        public async ValueTask Can_async_dispose()
        {
            // In C# 8 the below is "async using" construct
            LocalFileAsync observed = new LocalFileAsync();
            try
            {
                Assert.False(observed.IsDisposed);
            }
            finally
            {
                await observed.DisposeAsync();
            }
            Assert.True(observed.IsDisposed);
        }
    }
}
