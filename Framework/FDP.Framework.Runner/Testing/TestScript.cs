namespace FDP.Framework.Runner.Testing
{
    /// <summary>
    /// Root model for a headless test script JSON file.
    /// </summary>
    public class TestScript
    {
        /// <summary>Human-readable test name, used in the report.</summary>
        public string TestName { get; set; } = string.Empty;

        /// <summary>Total duration of the test run in seconds.</summary>
        public double Duration { get; set; }

        /// <summary>Ordered list of steps to execute during the test.</summary>
        public List<TestStep> Steps { get; set; } = new();
    }

    /// <summary>
    /// A single timed action within a <see cref="TestScript"/>.
    /// </summary>
    public class TestStep
    {
        /// <summary>Simulation time (seconds from start) at which this action fires.</summary>
        public double Time { get; set; }

        /// <summary>Action name that maps to a registered <c>ITestActionHandler</c>.</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>Arbitrary key-value arguments forwarded to the handler.</summary>
        public Dictionary<string, object> Args { get; set; } = new();

        /// <summary>
        /// Optional assertion rules checked against the handler's return value.
        /// Key is the metric name; value is the assertion rule.
        /// </summary>
        public Dictionary<string, AssertionRule>? Assert { get; set; }

        /// <summary>
        /// Number of times to repeat this step. Defaults to 1 (run once).
        /// Combined with <see cref="Interval"/> to produce multiple steps at
        /// <c>Time</c>, <c>Time + Interval</c>, <c>Time + 2*Interval</c>, …
        /// </summary>
        public int Repeat { get; set; } = 1;

        /// <summary>Seconds between each repeated invocation when <see cref="Repeat"/> > 1.</summary>
        public double Interval { get; set; }
    }

    /// <summary>
    /// Assertion rule used to validate a numeric metric after a step executes.
    /// Only non-null fields are evaluated.
    /// </summary>
    public class AssertionRule
    {
        /// <summary>Inclusive lower bound. Fails if the value is less than this.</summary>
        public double? Min { get; set; }

        /// <summary>Inclusive upper bound. Fails if the value is greater than this.</summary>
        public double? Max { get; set; }

        /// <summary>Exact equality check (tolerance: 0.001). Fails if value differs.</summary>
        public double? Equals { get; set; }
    }
}
