using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Fdp.Toolkit.Runner.Testing
{
    /// <summary>
    /// Drives a <see cref="SubsystemOrchestrator"/> in headless mode, executing a
    /// time-sequenced <see cref="TestScript"/> and collecting pass/fail metrics.
    ///
    /// <para>Built-in action handlers: <c>wait</c>, <c>assert_all</c>, <c>tick</c>.
    /// Register domain-specific handlers (e.g. <c>spawn</c>) via
    /// <see cref="RegisterHandler"/> before calling <see cref="RunAsync"/>.</para>
    ///
    /// <para>Usage:</para>
    /// <code>
    /// var executor = new HeadlessTestExecutor(orchestrator, "test_basic.json", logger);
    /// executor.RegisterHandler(new SpawnActionHandler(world, logger));
    /// int exitCode = await executor.RunAsync();
    /// </code>
    /// </summary>
    public class HeadlessTestExecutor
    {
        private readonly SubsystemOrchestrator _orchestrator;
        private readonly TestScript _script;
        private readonly ILogger _logger;
        private readonly Dictionary<string, ITestActionHandler> _actionHandlers;
        private readonly TestMetricsCollector _metrics;
        private readonly List<string> _assertionFailures = new();

        /// <summary>
        /// Stores the return values of steps that specify <see cref="TestStep.SaveResult"/>.
        /// Steps in later script entries can resolve an <c>entity_ref</c> argument to the
        /// corresponding saved <c>entity_id</c> by looking up the key here.
        /// </summary>
        public readonly Dictionary<string, object?> SavedResults =
            new(StringComparer.OrdinalIgnoreCase);

        // Populated during RunAsync and read by GenerateReport.
        private readonly Stopwatch _runStopwatch = new();
        private int _totalAssertionChecks;

        // ── Construction ─────────────────────────────────────────────────────

        public HeadlessTestExecutor(SubsystemOrchestrator orchestrator, string scriptPath, ILogger logger)
        {
            _orchestrator   = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger         = logger       ?? throw new ArgumentNullException(nameof(logger));
            _script         = LoadScript(scriptPath);
            _metrics        = new TestMetricsCollector();
            _actionHandlers = new Dictionary<string, ITestActionHandler>(StringComparer.OrdinalIgnoreCase);
            RegisterBuiltInHandlers();
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Optional callback invoked after <see cref="SubsystemOrchestrator.Initialize"/> completes
        /// but before the orchestrator run loop starts.
        /// Use this to register additional ECS components or systems that must be present
        /// from the first simulation frame (e.g. test-only <c>MovingEntitySystem</c>).
        /// </summary>
        public Action? AfterInitialize { get; set; }

        /// <summary>
        /// Runs the full test: initialises subsystems, executes all steps, waits for
        /// <see cref="TestScript.Duration"/>, shuts down, and returns 0 (pass) or 1 (fail).
        /// </summary>
        public async Task<int> RunAsync()
        {
            _runStopwatch.Restart();
            _logger.LogInformation("Starting test: {TestName}", _script.TestName);

            // 1. Initialise the orchestrator (headless — no Raylib window).
            _orchestrator.Initialize();

            // 1a. Optional post-init setup (e.g. register test-only ECS components/systems).
            AfterInitialize?.Invoke();

            // 2. Run the orchestrator update loop in a background thread so we can
            //    schedule async step dispatch from this task concurrently.
            var loopTask = Task.Run(() => _orchestrator.Run());

            try
            {
                // 3. Execute steps at their scheduled times.
                var stopwatch = Stopwatch.StartNew();
                foreach (var step in _script.Steps.OrderBy(s => s.Time))
                {
                    await WaitUntilTime(stopwatch, step.Time).ConfigureAwait(false);
                    await ExecuteStep(step).ConfigureAwait(false);
                }

                // 4. Idle until duration expires.
                while (stopwatch.Elapsed.TotalSeconds < _script.Duration)
                    await Task.Delay(100).ConfigureAwait(false);
            }
            finally
            {
                // 5. Stop the orchestrator loop and shut down.
                _orchestrator.Stop();
                await loopTask.ConfigureAwait(false);

                _orchestrator.Shutdown();
            }

            // 6. Generate report and return exit code.
            var report = GenerateReport();
            SaveReport(report);

            bool passed = _assertionFailures.Count == 0;
            _logger.LogInformation("Test {Status}: {TestName}", passed ? "PASSED" : "FAILED", _script.TestName);
            return passed ? 0 : 1;
        }

        /// <summary>
        /// Registers a custom action handler. Call before <see cref="RunAsync"/>.
        /// </summary>
        public void RegisterHandler(ITestActionHandler handler)
        {
            _actionHandlers[handler.ActionName] = handler;
        }

        // ── Script loading ────────────────────────────────────────────────────

        public static TestScript LoadScript(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Test script not found: {path}", path);

            var json = File.ReadAllText(path, Encoding.UTF8);
            var script = JsonConvert.DeserializeObject<TestScript>(json)
                ?? throw new InvalidOperationException($"Failed to deserialize test script from '{path}'.");

            if (script.Duration <= 0)
                throw new InvalidOperationException($"Script '{path}': Duration must be > 0, got {script.Duration}.");

            if (!script.Steps.Any())
                throw new InvalidOperationException($"Script '{path}': must have at least one step.");

            script.Steps = ExpandRepeats(script.Steps);
            return script;
        }

        public static List<TestStep> ExpandRepeats(List<TestStep> steps)
        {
            var expanded = new List<TestStep>(steps.Count);

            foreach (var step in steps)
            {
                int count = Math.Max(1, step.Repeat);
                for (int i = 0; i < count; i++)
                {
                    var clone = JsonConvert.DeserializeObject<TestStep>(JsonConvert.SerializeObject(step))!;
                    clone.Time = step.Time + i * step.Interval;
                    expanded.Add(clone);
                }
            }

            return expanded;
        }

        // ── Step execution ─────────────────────────────────────────────────────

        private async Task ExecuteStep(TestStep step)
        {
            _logger.LogInformation("[{Time:F2}s] Executing: {Action}", step.Time, step.Action);

            if (!_actionHandlers.TryGetValue(step.Action, out var handler))
            {
                var msg = $"No handler registered for action '{step.Action}'.";
                _logger.LogError(msg);
                _assertionFailures.Add(msg);
                return;
            }

            // Resolve entity_ref arguments: replace the string reference key with the
            // saved entity_id integer from a previous step's SaveResult.
            var resolvedArgs = ResolveEntityRefs(step.Args);

            object? result;
            try
            {
                result = await handler.ExecuteAsync(resolvedArgs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var msg = $"Action '{step.Action}' threw: {ex.Message}";
                _logger.LogError(ex, "Action handler threw exception");
                _assertionFailures.Add(msg);
                return;
            }

            // Persist the result under the requested key so later steps can reference it.
            if (!string.IsNullOrEmpty(step.SaveResult))
            {
                SavedResults[step.SaveResult] = result;
                _logger.LogDebug("Saved result '{Key}' = {Result}", step.SaveResult, result);
            }

            if (step.Assert != null)
                ValidateAssertions(step.Assert, result, step.Action);
        }

        /// <summary>
        /// For every argument value that is a string matching a key in <see cref="SavedResults"/>,
        /// replaces <c>"entity_ref"</c> keys with the entity_id stored in the saved result.
        /// </summary>
        private Dictionary<string, object> ResolveEntityRefs(Dictionary<string, object> args)
        {
            if (!args.ContainsKey("entity_ref"))
                return args;

            var resolved = new Dictionary<string, object>(args, StringComparer.OrdinalIgnoreCase);
            if (resolved.TryGetValue("entity_ref", out var refKeyObj) &&
                refKeyObj is string refKey &&
                SavedResults.TryGetValue(refKey, out var savedResult) &&
                savedResult is IDictionary<string, object> savedDict &&
                savedDict.TryGetValue("entity_id", out var entityId))
            {
                resolved.Remove("entity_ref");
                resolved["entity_id"] = entityId;
            }
            return resolved;
        }

        private void ValidateAssertions(
            Dictionary<string, AssertionRule> assertions, object? result, string stepAction)
        {
            foreach (var (metricName, rule) in assertions)
            {
                _totalAssertionChecks++;
                double value;
                try
                {
                    value = GetMetricValue(result, metricName);
                }
                catch (Exception ex)
                {
                    var msg = $"[{stepAction}] Cannot read metric '{metricName}': {ex.Message}";
                    _logger.LogWarning(msg);
                    _assertionFailures.Add(msg);
                    continue;
                }

                _metrics.RecordMetric(metricName, value);

                if (rule.Min.HasValue && value < rule.Min.Value)
                {
                    var msg = $"[{stepAction}] ASSERT FAIL: {metricName} = {value:F4}, expected >= {rule.Min}";
                    _logger.LogError(msg);
                    _assertionFailures.Add(msg);
                }

                if (rule.Max.HasValue && value > rule.Max.Value)
                {
                    var msg = $"[{stepAction}] ASSERT FAIL: {metricName} = {value:F4}, expected <= {rule.Max}";
                    _logger.LogError(msg);
                    _assertionFailures.Add(msg);
                }

                if (rule.Exactly.HasValue && Math.Abs(value - rule.Exactly.Value) > 0.001)
                {
                    var msg = $"[{stepAction}] ASSERT FAIL: {metricName} = {value:F4}, expected == {rule.Exactly}";
                    _logger.LogError(msg);
                    _assertionFailures.Add(msg);
                }

                if (rule.ApproxEquals.HasValue)
                {
                    double tol = rule.Tolerance > 0 ? rule.Tolerance : 0.001;
                    if (Math.Abs(value - rule.ApproxEquals.Value) > tol)
                    {
                        var msg = $"[{stepAction}] ASSERT FAIL: {metricName} = {value:F6}, expected ≈ {rule.ApproxEquals} ±{tol}";
                        _logger.LogError(msg);
                        _assertionFailures.Add(msg);
                    }
                }
            }
        }

        private static double GetMetricValue(object? result, string metricName)
        {
            if (result == null)
                throw new InvalidOperationException("Handler returned null — cannot evaluate metric.");

            if (result is double d)
                return metricName.Equals("value", StringComparison.OrdinalIgnoreCase) ? d : throw new KeyNotFoundException(metricName);
            if (result is int i)
                return metricName.Equals("value", StringComparison.OrdinalIgnoreCase) ? i : throw new KeyNotFoundException(metricName);
            if (result is long l)
                return metricName.Equals("value", StringComparison.OrdinalIgnoreCase) ? l : throw new KeyNotFoundException(metricName);

            if (result is IDictionary<string, object> dict)
            {
                if (!dict.TryGetValue(metricName, out var raw))
                    throw new KeyNotFoundException($"Result dictionary has no key '{metricName}'.");
                return Convert.ToDouble(raw);
            }

            var prop = result.GetType().GetProperty(metricName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance);
            if (prop == null)
                throw new KeyNotFoundException($"Cannot find metric '{metricName}' on result of type '{result.GetType().Name}'.");

            return Convert.ToDouble(prop.GetValue(result));
        }

        // ── Timing ────────────────────────────────────────────────────────────

        private static async Task WaitUntilTime(Stopwatch sw, double targetSeconds)
        {
            while (sw.Elapsed.TotalSeconds < targetSeconds)
                await Task.Delay(10).ConfigureAwait(false);
        }

        // ── Built-in handler registration ─────────────────────────────────────

        private void RegisterBuiltInHandlers()
        {
            RegisterHandler(new WaitActionHandler(_logger));
            RegisterHandler(new AssertAllActionHandler(_metrics, _logger));
            RegisterHandler(new TickActionHandler(_orchestrator, _logger));
        }

        // ── Reporting ─────────────────────────────────────────────────────────

        private TestReport GenerateReport()
        {
            var report = new TestReport
            {
                TestName        = _script.TestName,
                Status          = _assertionFailures.Count == 0 ? "PASS" : "FAIL",
                DurationSeconds = _runStopwatch.Elapsed.TotalSeconds,
                Errors          = new List<string>(_assertionFailures),
            };

            foreach (var name in _metrics.MetricNames)
            {
                if (_metrics.HasMetric(name))
                    report.Metrics[name] = _metrics.GetSummary(name);
            }

            report.Assertions.Total  = _totalAssertionChecks;
            report.Assertions.Failed = _assertionFailures.Count;
            report.Assertions.Passed = _totalAssertionChecks - _assertionFailures.Count;

            return report;
        }

        private void SaveReport(TestReport report)
        {
            var filename = $"test_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var json     = JsonConvert.SerializeObject(report, Formatting.Indented);
            try
            {
                File.WriteAllText(filename, json);
                _logger.LogInformation("Report saved to {Path}", filename);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save report to {Path}", filename);
            }

            Console.WriteLine();
            Console.WriteLine("=== TEST RESULTS ===");
            Console.WriteLine($"Test:     {report.TestName}");
            Console.WriteLine($"Status:   {report.Status}");
            Console.WriteLine($"Duration: {report.DurationSeconds:F2}s");

            if (report.Metrics.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Metrics:");
                foreach (var (name, m) in report.Metrics)
                    Console.WriteLine($"  {name}: min={m.Min:F1}, max={m.Max:F1}, avg={m.Avg:F1}, p95={m.P95:F1}");
            }

            Console.WriteLine();
            Console.WriteLine($"Assertions: {report.Assertions.Passed}/{report.Assertions.Total} passed");
            Console.WriteLine($"Report saved to: {filename}");
        }

        // ── Built-in handler implementations ─────────────────────────────────

        private sealed class WaitActionHandler : ITestActionHandler
        {
            private readonly ILogger _log;
            public string ActionName => "wait";
            public WaitActionHandler(ILogger log) => _log = log;

            public async Task<object?> ExecuteAsync(Dictionary<string, object> args)
            {
                double seconds = args.TryGetValue("seconds", out var v) ? Convert.ToDouble(v) : 0;
                _log.LogDebug("Wait {Seconds:F2}s", seconds);
                await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                return null;
            }
        }

        private sealed class AssertAllActionHandler : ITestActionHandler
        {
            private readonly TestMetricsCollector _metrics;
            private readonly ILogger _log;
            public string ActionName => "assert_all";

            public AssertAllActionHandler(TestMetricsCollector metrics, ILogger log)
            {
                _metrics = metrics;
                _log     = log;
            }

            public Task<object?> ExecuteAsync(Dictionary<string, object> args)
            {
                var elapsed = _metrics.HasMetric("duration")
                    ? _metrics.GetSummary("duration").Avg
                    : 0.0;
                return Task.FromResult<object?>(new Dictionary<string, object> { ["duration"] = elapsed });
            }
        }

        private sealed class TickActionHandler : ITestActionHandler
        {
            private readonly SubsystemOrchestrator _orch;
            private readonly ILogger _log;
            public string ActionName => "tick";

            public TickActionHandler(SubsystemOrchestrator orch, ILogger log)
            {
                _orch = orch;
                _log  = log;
            }

            public Task<object?> ExecuteAsync(Dictionary<string, object> args)
            {
                int frames = args.TryGetValue("frames", out var v) ? Convert.ToInt32(v) : 1;
                _log.LogDebug("Tick {Frames} frame(s)", frames);
                _orch.RunFrames(frames);
                return Task.FromResult<object?>(new Dictionary<string, object> { ["frames_run"] = frames });
            }
        }
    }
}
