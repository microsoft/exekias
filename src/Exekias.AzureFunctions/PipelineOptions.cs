namespace Exekias.AzureFunctions
{
    public class PipelineOptions
    {
        public const string PipelineOptionsSection = "Pipeline";
        public int ThresholdSeconds { get; set; } = 30;
        public int RunCacheTimeoutHours { get; set; } = 48;
    }
}
