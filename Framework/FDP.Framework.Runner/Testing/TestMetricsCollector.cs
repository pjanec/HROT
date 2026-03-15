using Fdp.Kernel;

namespace FDP.Framework.Runner.Testing
{
    /// <summary>
    /// Thread-safe collector for numeric metrics sampled during a headless test run.
    /// After the run completes, per-metric summaries can be retrieved for assertions
    /// and report generation.
    /// </summary>
    public class TestMetricsCollector
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<double>> _samples = new();
        private readonly object _writeLock = new();

        // ── Write ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Records a single <paramref name="value"/> sample for the named metric.
        /// Safe to call from multiple threads.
        /// </summary>
        public void RecordMetric(string name, double value)
        {
            var bucket = _samples.GetOrAdd(name, _ => new List<double>());
            lock (_writeLock)
            {
                bucket.Add(value);
            }
        }

        /// <summary>
        /// Samples the current state of the ECS <paramref name="world"/> and records
        /// the following metrics:
        /// <list type="bullet">
        ///   <item><c>entity_count</c> — number of live entities.</item>
        ///   <item><c>frame_duration_ms</c> — provided <paramref name="frameMs"/> value (ms).</item>
        /// </list>
        /// </summary>
        /// <param name="world">The ECS world to sample. No-op when <see langword="null"/>.</param>
        /// <param name="frameMs">Duration of the most recent simulation frame in milliseconds.</param>
        public void SampleWorld(EntityRepository? world, double frameMs = 0)
        {
            if (world != null)
                RecordMetric("entity_count", world.EntityCount);

            if (frameMs > 0)
                RecordMetric("frame_duration_ms", frameMs);
        }

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a statistical summary for <paramref name="name"/>.
        /// </summary>
        public MetricSummary GetSummary(string name)
        {
            if (!_samples.TryGetValue(name, out var bucket))
                throw new KeyNotFoundException($"No metric samples recorded for '{name}'.");

            List<double> snapshot;
            lock (_writeLock)
            {
                snapshot = new List<double>(bucket);
            }

            if (snapshot.Count == 0)
                throw new InvalidOperationException($"Metric '{name}' has no samples.");

            var sorted = snapshot.OrderBy(v => v).ToList();
            return new MetricSummary
            {
                Name    = name,
                Count   = sorted.Count,
                Min     = sorted.First(),
                Max     = sorted.Last(),
                Avg     = sorted.Average(),
                P95     = CalculatePercentile(sorted, 0.95)
            };
        }

        /// <summary>Whether any samples exist for <paramref name="name"/>.</summary>
        public bool HasMetric(string name) => _samples.ContainsKey(name);

        /// <summary>All metric names that have been recorded.</summary>
        public IEnumerable<string> MetricNames => _samples.Keys;

        // ── Helpers ──────────────────────────────────────────────────────────

        private static double CalculatePercentile(List<double> sorted, double percentile)
        {
            if (sorted.Count == 1) return sorted[0];

            double index  = percentile * (sorted.Count - 1);
            int    lower  = (int)Math.Floor(index);
            int    upper  = (int)Math.Ceiling(index);
            double weight = index - lower;
            return sorted[lower] * (1 - weight) + sorted[upper] * weight;
        }
    }
}
