using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Exekias.Core
{
    /// <summary>
    /// Abstract file store where Runs write data and metadata.
    /// </summary>
    public interface IRunStore
    {

        string AbsoluteBasePath { get; }

        /// <summary>
        /// Takes path to a directory containing all files of a Run from a parsed metadata file path of the Run.
        /// </summary>
        /// <param name="metadataFilePath">Parsed full path of the Run metadata file.</param>
        /// <returns>Run directory path.</returns>
        RunFile CreateRunFile(string filePath, DateTimeOffset lastWriteTime);

        /// <summary>
        /// Get descriptors of all tracked files in the store.
        /// </summary>
        /// <returns>A promise of list of file descriptors.</returns>
        Task<IEnumerable<RunFile>> GetAllFiles();

        /// <summary>
        /// Get descriptors of all tracked files and group them by run.
        /// </summary>
        /// <returns></returns>
        Task<Dictionary<string, RunRecord>> GetAllRuns();

        /// <summary>
        /// Find all importable files under a Run directory except metadata file.
        /// </summary>
        /// <param name="runPath">Full path of the Run directory.</param>
        /// <returns>A stream of RunFile objects.</returns>
        IAsyncEnumerable<RunFile> GetDataFilesUnder(string runPath);

        /// <summary>
        /// Get local file corresponding to a Run file.
        /// </summary>
        /// <param name="runFilePath">Full path of the Run file.</param>
        /// <returns>An object containg properties of the Run file and full path to the Local file.</returns>
        ValueTask<LocalFile> GetLocalFile(string runFilePath);
    }


    /// <summary>
    /// A collection of 
    /// </summary>
    public class RunData
    {
        public string RunPath { get; }
        public FileShot? Meta { get; }
        public List<FileShot> Data { get; }
        public RunData(string runPath, FileShot? meta, List<FileShot> data)
        {
            RunPath = runPath;
            Meta = meta;
            Data = data;
        }

        // Cannot have second contructor, use factory instead
        // Newtonsoft.Json requires either a default constructor, one constructor with arguments or a constructor marked with the JsonConstructor attribute.

        public static RunData Create(string runPath, FileShot? meta, params FileShot[] data)
            => new RunData(runPath, meta, data.ToList());

    }


    /// <summary>
    /// An entry to store descriptors of a metadata file together with all associated data files.
    /// </summary>
    public class RunRecord
    {
        /// <summary>
        /// Constructs an instance of the record with an empty list of Sata files.
        /// </summary>
        /// <param name="meta">Metadata file descriptor.</param>
        public RunRecord(RunFile? meta, params RunFile[] data)
        {
            Meta = meta;
            Data = new List<RunFile>(data);
        }
        /// <summary>
        /// Run metadata file descriptor.
        /// </summary>
        public RunFile? Meta { get; set; }
        /// <summary>
        /// Run data file descriptors.
        /// </summary>
        public List<RunFile> Data { get; }


        public RunData AsData(string runPath)
        {
            if (null != Meta && runPath != Meta.RunPath())
            {
                throw new InvalidOperationException();
            }
            return new RunData(
                runPath,
                Meta?.AsTuple(),
                Data.ConvertAll(item => item.AsTuple()));
        }
    }
}
