using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Exekias.Core.Tests
{
    /// <summary>
    /// Provides temporary directory to an xUnit test class.
    /// </summary>
    /// <remarks>
    /// Mark test class with <see cref="IClassFixture{WorkDirFixture}"/> interface,
    /// and you get <see cref="WorkDirFixture"/> instance as a constructor argument.
    /// <see cref="Xunit"/> runner takes care of instantiating and disposing of the instance.
    /// </remarks>
    public sealed class WorkDirFixture: IDisposable
    {
        public string LocalPath { get; }
        public WorkDirFixture()
        {
            LocalPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(LocalPath);
        }
        public void Dispose()
        {
            Directory.Delete(LocalPath, true);
        }
    }
}
