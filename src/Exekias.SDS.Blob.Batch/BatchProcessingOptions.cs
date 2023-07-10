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
        public string? Name { get; set; }
        public string? Endpoint { get; set; }
        public string? AccessKey { get; set; }
        public string PoolId { get; set; } = "exekias";
        public string PoolScaleFormula { get; set; } = @"";
        public string VmImagePublisher { get; set; } = "MicrosoftWindowsServer";
        public string VmImageOffer { get; set; } = "WindowsServer";
        public string VmImageSKU { get; set; } = "2022-datacenter-core-smalldisk";
        public string VmAgentSKU { get; set; } = "batch.node.windows amd64";
        public string VmSize { get; set; } = "standard_d1_v2";
        public string AppPackageId { get; set; } = "dataimport";
        public string AppPackageVersion { get; set; } = "1.0.0";
        public string AppPackageExe { get; set; } = "Exekias.DataImport.exe";
    }
}