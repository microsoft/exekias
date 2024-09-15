namespace Exekias.SDS.Blob.Batch
{
    /// <summary>
    /// Options for batch processng.
    /// </summary>
    /// <param name="Name">Batch account name.</param>
    /// <param name="Endpoint">Account endpoint.</param>
    /// <param name="AccessKey">Shared access key.</param>
    public class BatchProcessingOptions
    {
        public string? Endpoint { get; set; }
        public string PoolId { get; set; } = "exekias";
        public string AppPackageId { get; set; } = "dataimport";
        public string AppPackageVersion { get; set; } = "1.0.0";
        public string AppPackageExe { get; set; } = "Exekias.DataImport.exe";
    }
}