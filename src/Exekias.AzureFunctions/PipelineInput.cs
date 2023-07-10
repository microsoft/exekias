using System;
using System.Collections.Generic;

namespace Exekias.AzureFunctions
{
    class PipelineInput
    {
        public int ThresholdSeconds { get; set; } = 30;
        public int RunCacheTimeoutHours { get; set; } = 48;
        /// <summary>
        /// A cache of active runs: Run ID -> Last touched UTC.
        /// </summary>
        public Dictionary<string, DateTimeOffset> runCache { get; set; } = new Dictionary<string, DateTimeOffset>();
    }

}
