using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Exekias.Core.Tests
{
    public class TempFileTests
    {
        [Fact]
        public void Constructor_argument_must_not_be_null()
        {
            Assert.Throws<ArgumentNullException>(() =>
            new LocalTempFile(null));
        }

        [Fact]
        public void Temp_file_deleted_on_dispose()
        {
            string path;
            using (var observed = new LocalTempFile("//host/directory/name.csv"))
            {
                path = observed.LocalPath;
                Assert.True(File.Exists(path));
            }
            Assert.False(File.Exists(path));
        }
    }
}
