//
// Command line utility to import a single run blob
// to an ImportStore and update corresponding file and run objects in MetadataStore.
//
// > Exekias.DataImport <run> <file>
//
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

/// <summary>
/// Adds operation specific properties to Application Insights trace.
/// </summary>
public class DataImportTelemetryInitializer : ITelemetryInitializer
{
    private readonly string _runId;
    private readonly string _runFile;
    private readonly string _jobId = Environment.GetEnvironmentVariable("AZ_BATCH_JOB_ID") ?? "(empty)";
    private readonly string _taskId = Environment.GetEnvironmentVariable("AZ_BATCH_TASK_ID") ?? "(empty)";
    public DataImportTelemetryInitializer(string runId, string runFile)
    {
        _runId = runId;
        _runFile = runFile;
    }

    public void Initialize(ITelemetry telemetry)
    {
        telemetry.Context.Operation.Name = "DataImportTask";
        telemetry.Context.GlobalProperties["RunId"] = _runId;
        telemetry.Context.GlobalProperties["RunFile"] = _runFile;
        telemetry.Context.GlobalProperties["JobId"] = _jobId;
        telemetry.Context.GlobalProperties["TaskId"] = _taskId;
    }
}