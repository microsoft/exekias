using System.CommandLine;

namespace exekiascmd
{
    internal record ProgressIndicator(IConsole console, double frequencySeconds = 1)
    {
        private List<Progress> progressList = new List<Progress>();
        private long total = 0;
        public IProgress<long> NewProgress(long Total)
        {
            var progress = new Progress(Signal, Total);
            total += Total;
            progressList.Add(progress);
            return progress;
        }

        private DateTime firstUpdate = DateTime.MinValue;
        private DateTime lastUpdate = DateTime.MinValue;
        private void Signal()
        {
            var now = DateTime.Now;
            if ((now - lastUpdate).TotalSeconds > frequencySeconds)
            {
                if (firstUpdate == DateTime.MinValue) { firstUpdate = now; }
                lastUpdate = now;
                int count = 0;
                long value = 0;
                foreach (var progress in progressList)
                {
                    value += progress.Value;
                    if (progress.Value >= progress.Total) count += 1;
                }
                var elapsed = (DateTime.Now - firstUpdate).TotalSeconds;
                if (value > 0)
                {
                    var eta = TimeSpan.FromSeconds(Math.Round(elapsed * total / value - elapsed));
                    console.Write($"Files: {count} of {progressList.Count}, {fmt(value)} of {fmt(total)}, ETA {eta:g}                    \r");
                }
            }
        }

        static string fmt(long bytes)
        {
            const double KB = 1024;
            const double MB = 1024 * KB;
            const double GB = 1024 * MB;
            if (bytes > 100 * GB) return $"{bytes / GB:F0} GB";
            if (bytes > GB) return $"{bytes / GB:F2} GB";
            if (bytes > 100 * MB) return $"{bytes / MB:F0} MB";
            if (bytes > MB) return $"{bytes / MB:F2} MB";
            if (bytes > 100 * KB) return $"{bytes / KB:F0} KB";
            if (bytes > KB) return $"{bytes / KB:F2} KB";
            return $"{bytes} B";
        }
        record Progress(Action Signal, long Total) : IProgress<long>
        {
            public long Value { get; private set; } = 0;

            public void Report(long value)
            {
                Value = value;
                Signal();
            }
        }

        public void Flush(string suffix)
        {
            var elapsed = TimeSpan.FromSeconds(Math.Round((DateTime.Now - firstUpdate).TotalSeconds));
            console.WriteLine($"{progressList.Count} files, {fmt(total)} in {elapsed:g}{suffix}                         ");
            progressList.Clear();
        }
    }
}
