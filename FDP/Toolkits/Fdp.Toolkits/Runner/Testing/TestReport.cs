namespace Fdp.Toolkit.Runner.Testing
{
    /// <summary>Statistical summary for a named metric.</summary>
    public sealed class MetricSummary
    {
        public string Name    { get; init; } = string.Empty;
        public int    Count   { get; init; }
        public double Min     { get; init; }
        public double Max     { get; init; }
        public double Avg     { get; init; }
        /// <summary>95th-percentile value.</summary>
        public double P95     { get; init; }
    }

    /// <summary>
    /// Structured JSON report generated at the end of a headless test run.
    /// Saved as <c>test_report_{timestamp}.json</c> in the working directory.
    /// </summary>
    public class TestReport
    {
        /// <summary>Human-readable name from the test script.</summary>
        public string TestName { get; set; } = string.Empty;

        /// <summary><c>"PASS"</c> or <c>"FAIL"</c>.</summary>
        public string Status { get; set; } = "PASS";

        /// <summary>Wall-clock duration of the test run in seconds.</summary>
        public double DurationSeconds { get; set; }

        /// <summary>Per-metric statistical summaries (min / max / avg / p95).</summary>
        public Dictionary<string, MetricSummary> Metrics { get; set; } = new();

        /// <summary>Total, passed, and failed assertion counts.</summary>
        public AssertionResults Assertions { get; set; } = new();

        /// <summary>Assertion failure messages and any uncaught exceptions.</summary>
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>Per-assertion result counts for a test run.</summary>
    public class AssertionResults
    {
        /// <summary>Total number of assertion rule checks evaluated.</summary>
        public int Total { get; set; }

        /// <summary>Number of assertion rule checks that passed.</summary>
        public int Passed { get; set; }

        /// <summary>Number of assertion rule checks that failed.</summary>
        public int Failed { get; set; }
    }
}
