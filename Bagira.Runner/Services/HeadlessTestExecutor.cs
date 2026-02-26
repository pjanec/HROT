using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Bagira.Runner.Models;
using Fdp.Kernel;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// Drives a <see cref="SubsystemOrchestrator"/> in headless mode, executing a
    /// time-sequenced <see cref="TestScript"/> and collecting pass/fail metrics.
    ///
    /// <para>Usage:</para>
    /// <code>
    /// var executor = new HeadlessTestExecutor(orchestrator, "test_basic.json", logger);
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
        private readonly EntityRepository? _world;

        // ── Construction ─────────────────────────────────────────────────────

        public HeadlessTestExecutor(SubsystemOrchestrator orchestrator, string scriptPath, ILogger logger, EntityRepository? world = null)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
            _script       = LoadScript(scriptPath);
            _metrics      = new TestMetricsCollector();
            _world        = world;
            _actionHandlers = new Dictionary<string, ITestActionHandler>(StringComparer.OrdinalIgnoreCase);

            RegisterActionHandlers();
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Runs the full test: initialises subsystems, executes all steps, waits for
        /// <see cref="TestScript.Duration"/>, shuts down, and returns 0 (pass) or 1 (fail).
        /// </summary>
        public async Task<int> RunAsync()
        {
            _logger.LogInformation("Starting test: {TestName}", _script.TestName);

            // 1. Initialise the orchestrator (headless — no Raylib window).
            _orchestrator.Initialize();

            // 2. Run the orchestrator update loop in a background thread so we can
            //    schedule async step dispatch from this task concurrently.
            //    SubsystemOrchestrator.Run() blocks until Stop() is called.
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

        /// <summary>
        /// Loads, validates, and expands repeat-blocks in a test script JSON file.
        /// </summary>
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

        /// <summary>
        /// Expands steps with <c>Repeat &gt; 1</c> into individual steps spaced by
        /// <c>Interval</c> seconds.
        /// </summary>
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

            object? result;
            try
            {
                result = await handler.ExecuteAsync(step.Args).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var msg = $"Action '{step.Action}' threw: {ex.Message}";
                _logger.LogError(ex, "Action handler threw exception");
                _assertionFailures.Add(msg);
                return;
            }

            if (step.Assert != null)
                ValidateAssertions(step.Assert, result, step.Action);
        }

        private void ValidateAssertions(
            Dictionary<string, AssertionRule> assertions, object? result, string stepAction)
        {
            foreach (var (metricName, rule) in assertions)
            {
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

                if (rule.Equals.HasValue && Math.Abs(value - rule.Equals.Value) > 0.001)
                {
                    var msg = $"[{stepAction}] ASSERT FAIL: {metricName} = {value:F4}, expected == {rule.Equals}";
                    _logger.LogError(msg);
                    _assertionFailures.Add(msg);
                }
            }
        }

        private static double GetMetricValue(object? result, string metricName)
        {
            if (result == null)
                throw new InvalidOperationException("Handler returned null — cannot evaluate metric.");

            // If result is directly a number, use it for the special key "value".
            if (result is double d)
                return metricName.Equals("value", StringComparison.OrdinalIgnoreCase) ? d : throw new KeyNotFoundException(metricName);
            if (result is int i)
                return metricName.Equals("value", StringComparison.OrdinalIgnoreCase) ? i : throw new KeyNotFoundException(metricName);
            if (result is long l)
                return metricName.Equals("value", StringComparison.OrdinalIgnoreCase) ? l : throw new KeyNotFoundException(metricName);

            // If result is a dictionary, look up the metric by name.
            if (result is IDictionary<string, object> dict)
            {
                if (!dict.TryGetValue(metricName, out var raw))
                    throw new KeyNotFoundException($"Result dictionary has no key '{metricName}'.");
                return Convert.ToDouble(raw);
            }

            // Fall back to reflection.
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

        // ── Built-in handlers ─────────────────────────────────────────────────

        private void RegisterActionHandlers()
        {
            RegisterHandler(new WaitActionHandler(_logger));
            RegisterHandler(new AssertAllActionHandler(_metrics, _logger));
            RegisterHandler(new TickActionHandler(_orchestrator, _logger));
            RegisterHandler(new SpawnActionHandler(_world, _logger));
            RegisterHandler(new MoveActionHandler(_world, _logger));
            RegisterHandler(new AssertPositionActionHandler(_world, _logger));
        }

        // ── Reporting ─────────────────────────────────────────────────────────

        private TestReport GenerateReport()
        {
            return new TestReport
            {
                TestName         = _script.TestName,
                Status           = _assertionFailures.Count == 0 ? "PASS" : "FAIL",
                AssertionFailures = new List<string>(_assertionFailures),
                MetricNames      = new List<string>(_metrics.MetricNames)
            };
        }

        private void SaveReport(TestReport report)
        {
            // Always write the canonical TestRunSummary.json as well as the named report.
            var summaryPath = "TestRunSummary.json";
            var namedPath   = $"test-report-{report.TestName.Replace(' ', '_')}.json";
            var json        = JsonConvert.SerializeObject(report, Formatting.Indented);
            try
            {
                File.WriteAllText(namedPath,   json);
                File.WriteAllText(summaryPath, json);
                _logger.LogInformation("Report saved to {Path} and {Summary}", namedPath, summaryPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save report to {Path}", namedPath);
            }
        }

        // ── Inner types ───────────────────────────────────────────────────────

        private sealed class TestReport
        {
            public string TestName { get; set; } = string.Empty;
            public string Status   { get; set; } = "UNKNOWN";
            public List<string> AssertionFailures { get; set; } = new();
            public List<string> MetricNames       { get; set; } = new();
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
                // Returns the elapsed time as the "duration" metric so callers
                // can assert  {"duration": {"min": 1.0}}.
                var elapsed = _metrics.HasMetric("duration")
                    ? _metrics.GetSummary("duration").Avg
                    : 0.0;

                return Task.FromResult<object?>(new Dictionary<string, object>
                {
                    ["duration"] = elapsed
                });
            }
        }

        // ── tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advances the headless simulation by the requested number of frames.
        /// Args: <c>frames</c> (int, default 1) — number of update iterations.
        /// Returns: <c>{"frames_run": N}</c>.
        /// </summary>
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
                return Task.FromResult<object?>(new Dictionary<string, object>
                {
                    ["frames_run"] = frames
                });
            }
        }

        // ── spawn ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new entity in the ECS world at the given position.
        /// Args: <c>x</c>, <c>y</c>, <c>z</c> (double, default 0) — world-space position in metres.
        /// Returns: <c>{"entity_id": N}</c>.
        /// When no <see cref="EntityRepository"/> is available, logs a warning and returns -1.
        /// </summary>
        private sealed class SpawnActionHandler : ITestActionHandler
        {
            private readonly EntityRepository? _world;
            private readonly ILogger _log;

            public string ActionName => "spawn";

            public SpawnActionHandler(EntityRepository? world, ILogger log)
            {
                _world = world;
                _log   = log;
            }

            public Task<object?> ExecuteAsync(Dictionary<string, object> args)
            {
                if (_world == null)
                {
                    _log.LogWarning("spawn: no EntityRepository available — skipping.");
                    return Task.FromResult<object?>(new Dictionary<string, object> { ["entity_id"] = -1 });
                }

                float x = (float)(args.TryGetValue("x", out var vx) ? Convert.ToDouble(vx) : 0.0);
                float y = (float)(args.TryGetValue("y", out var vy) ? Convert.ToDouble(vy) : 0.0);
                float z = (float)(args.TryGetValue("z", out var vz) ? Convert.ToDouble(vz) : 0.0);

                var entity = _world.CreateEntity();
                _world.AddComponent(entity, new SimTransform
                {
                    Position = new Vector3(x, y, z),
                    Rotation = Quaternion.Identity
                });

                _log.LogDebug("spawn: created entity {Id} at ({X},{Y},{Z})", entity.Index, x, y, z);
                return Task.FromResult<object?>(new Dictionary<string, object>
                {
                    ["entity_id"] = entity.Index
                });
            }
        }

        // ── move ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Teleports an existing entity to a new world-space position.
        /// Args: <c>entity_id</c> (long), <c>x</c>, <c>y</c>, <c>z</c> (double, default 0).
        /// Returns: <c>{"moved": 1}</c> on success, <c>{"moved": 0}</c> if component absent.
        /// </summary>
        private sealed class MoveActionHandler : ITestActionHandler
        {
            private readonly EntityRepository? _world;
            private readonly ILogger _log;

            public string ActionName => "move";

            public MoveActionHandler(EntityRepository? world, ILogger log)
            {
                _world = world;
                _log   = log;
            }

            public Task<object?> ExecuteAsync(Dictionary<string, object> args)
            {
                if (_world == null)
                {
                    _log.LogWarning("move: no EntityRepository available — skipping.");
                    return Task.FromResult<object?>(new Dictionary<string, object> { ["moved"] = 0 });
                }

                int entityIdx = args.TryGetValue("entity_id", out var ve) ? Convert.ToInt32(ve) : -1;
                float x = (float)(args.TryGetValue("x", out var vx) ? Convert.ToDouble(vx) : 0.0);
                float y = (float)(args.TryGetValue("y", out var vy) ? Convert.ToDouble(vy) : 0.0);
                float z = (float)(args.TryGetValue("z", out var vz) ? Convert.ToDouble(vz) : 0.0);

                var entity = _world.GetEntityByIndex(entityIdx);
                if (!_world.HasComponent<SimTransform>(entity))
                {
                    _log.LogWarning("move: entity {Id} has no SimTransform — cannot move.", entityIdx);
                    return Task.FromResult<object?>(new Dictionary<string, object> { ["moved"] = 0 });
                }

                ref var transform = ref _world.GetComponentRW<SimTransform>(entity);
                transform.Position = new Vector3(x, y, z);

                _log.LogDebug("move: entity {Id} → ({X},{Y},{Z})", entityIdx, x, y, z);
                return Task.FromResult<object?>(new Dictionary<string, object> { ["moved"] = 1 });
            }
        }

        // ── assert_position ───────────────────────────────────────────────────

        /// <summary>
        /// Reads the <see cref="SimTransform.Position"/> of an entity and returns
        /// its components as a result dictionary for assertion.
        /// Args: <c>entity_id</c> (long).
        /// Returns: <c>{"x": F, "y": F, "z": F}</c>.
        /// </summary>
        private sealed class AssertPositionActionHandler : ITestActionHandler
        {
            private readonly EntityRepository? _world;
            private readonly ILogger _log;

            public string ActionName => "assert_position";

            public AssertPositionActionHandler(EntityRepository? world, ILogger log)
            {
                _world = world;
                _log   = log;
            }

            public Task<object?> ExecuteAsync(Dictionary<string, object> args)
            {
                if (_world == null)
                {
                    _log.LogWarning("assert_position: no EntityRepository available.");
                    return Task.FromResult<object?>(null);
                }

                int entityIdx = args.TryGetValue("entity_id", out var ve) ? Convert.ToInt32(ve) : -1;
                var entity = _world.GetEntityByIndex(entityIdx);

                if (!_world.HasComponent<SimTransform>(entity))
                {
                    _log.LogWarning("assert_position: entity {Id} has no SimTransform.", entityIdx);
                    return Task.FromResult<object?>(null);
                }

                var transform = _world.GetComponent<SimTransform>(entity);
                _log.LogDebug("assert_position: entity {Id} at ({X},{Y},{Z})",
                    entityIdx, transform.Position.X, transform.Position.Y, transform.Position.Z);

                return Task.FromResult<object?>(new Dictionary<string, object>
                {
                    ["x"] = (double)transform.Position.X,
                    ["y"] = (double)transform.Position.Y,
                    ["z"] = (double)transform.Position.Z
                });
            }
        }
    }
}
