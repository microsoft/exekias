using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Exekias.Core
{
    /// <summary>
    /// Identifies a file snapshot.
    /// </summary>
    public class FileShot
    {
        /// <summary>
        /// File path relative to the root.
        /// </summary>
        public string Path { get; }
        /// <summary>
        /// Last write time for the file.
        /// </summary>
        public DateTimeOffset LastWriteTime { get; }
        /// <summary>
        /// Public constructor to create the record.
        /// </summary>
        /// <param name="path">File path relative to the root.</param>
        /// <param name="lastWriteTime"> Last write time for the file.</param>
        public FileShot(string path, DateTimeOffset lastWriteTime)
        {
            Path = path;
            LastWriteTime = lastWriteTime;
        }

        /// <summary>
        /// Strip a derived class.
        /// </summary>
        /// <returns>A clean FileShot copy.</returns>
        public FileShot ToFileShot() => new FileShot(this.Path, this.LastWriteTime);
    }
    /// <summary>
    /// Representation of a file in Run store.
    /// </summary>
    public abstract class RunFile: FileShot
    {
        protected RunFile(string fullPath, DateTimeOffset lastWriteTime) : base(fullPath, lastWriteTime)
        {
        }

        /// <summary>
        /// Strip to just a tuple of (Path, LastWriteWime)
        /// </summary>
        /// <returns></returns>
        public FileShot AsTuple() => ToFileShot();

        /// <summary>
        /// Indicates if the file path looks like a run metadata file path.
        /// </summary>
        /// <returns>True if the file is a metadata file path.</returns>
        public abstract bool IsMetadataFile();

        /// <summary>
        /// Gets run directory for a metadata file path.
        /// </summary>
        /// <returns>Run directory as inferred from the file path.</returns>
        /// <exception cref="InvalidOperationException">The path is not a metadata file path.</exception>
        public abstract string RunPath();
        /// <summary>
        /// Get metadata for a metadata file path.
        /// </summary>
        /// <returns>Metadata dictionary as inferred from the file path.</returns>
        /// <exception cref="InvalidOperationException">The path is not a metadata file path.</exception>
        public abstract Dictionary<string, object> PathMetadata();
    }

    /// <summary>
    /// Represents a local file, possibly temporary.
    /// </summary>
    public class LocalFile : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Disables default public constructor.
        /// </summary>
        protected LocalFile() { LocalPath = ""; }
        /// <summary>
        /// Path on a local file system.
        /// </summary>
        public string LocalPath { get; protected set; }

        /// <summary>
        /// Last write time for the local file or for the original Run file this local file represents.
        /// </summary>
        public virtual DateTimeOffset LastWriteTime => File.Exists(LocalPath) ? File.GetLastWriteTime(LocalPath) : DateTimeOffset.MinValue;
        /// <summary>
        /// Creates an object linked to the specified path.
        /// </summary>
        /// <param name="localPath">Path on a local file system.</param>
        /// <exception cref="ArgumentException">The <paramref name="localPath"/> is empty or has invalid characters.</exception>
        public LocalFile(string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath)
                || Path.GetInvalidPathChars().Any(c => localPath.Contains(c)))
                throw new ArgumentException("Invalid path", nameof(localPath));
            this.LocalPath = localPath;
        }
        /// <summary>
        /// Protected implementation of DisposeAsync pattern.
        /// </summary>
        /// <returns>A task that represents the asynchronous dispose operation.</returns>
        protected virtual ValueTask DisposeAsyncCore() => new ValueTask();
        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.
        /// </summary>
        /// <returns>A task that represents the asynchronous dispose operation.</returns>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();

            Dispose(false);
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Protected implementation of Dispose pattern.
        /// </summary>
        protected virtual void Dispose(bool disposing) { }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Represents a temporary file that have to be deleted when disposed.
    /// </summary>
    public class LocalTempFile : LocalFile
    {
        DateTimeOffset runFileLastWriteTime = DateTimeOffset.MinValue;
        public override DateTimeOffset LastWriteTime => runFileLastWriteTime;
        public void SetRunFileLastWriteTime(DateTimeOffset lastWriteTime) { runFileLastWriteTime = lastWriteTime; }

        /// <summary>
        /// Creates a temporary file with an extension of original file.
        /// </summary>
        /// <param name="runFilePath">The original file path.</param>
        /// <remarks>
        /// The <see cref="RunFile.Path"/> property takes the value of <paramref name="runFilePath"/>.
        /// Disposing the object deletes the temporary file.
        /// </remarks>
        public LocalTempFile(string runFilePath)
        {
            if (null == runFilePath) throw new ArgumentNullException(nameof(runFilePath));
            var tempFilePath = Path.GetTempFileName();
            new FileInfo(tempFilePath).Attributes = FileAttributes.Temporary;
            LocalPath = tempFilePath;
        }
        bool disposed = false;
        protected override void Dispose(bool disposing)
        {
            if (disposed) return;
            File.Delete(LocalPath);
            base.Dispose(disposing);
            disposed = true;
        }
    }

}
