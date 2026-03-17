using Fdp.Examples.Common;
using Fdp.Examples.Scenarios.Kinematics;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Vis2D;
using ModuleHost.Core;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;

// Disable parallel test execution across the entire assembly so NLog's global
// LogManager.Configuration is not modified by two tests simultaneously.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Fdp.Examples.Scenarios.Tests
{
    // ── Mock scenarios ────────────────────────────────────────────────────────

    /// <summary>Succeeds (returns true) at the specified tick.</summary>
    internal sealed class MockSucceedAtTickScenario : IScenario
    {
        private readonly uint _succeedAt;
        public string ScenarioName => "mocksucceed";

        public MockSucceedAtTickScenario(uint succeedAt) => _succeedAt = succeedAt;

        public void Configure(EntityRepository world, ModuleHostKernel kernel) { }
        public bool EvaluateTick(uint currentTick, EntityRepository world) => currentTick >= _succeedAt;
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }

    /// <summary>Throws <see cref="ScenarioFailureException"/> at the specified tick.</summary>
    internal sealed class MockFailAtTickScenario : IScenario
    {
        private readonly uint _failAt;
        private readonly string _message;
        public string ScenarioName => "mockfail";

        public MockFailAtTickScenario(uint failAt, string message = "assertion failed")
        {
            _failAt  = failAt;
            _message = message;
        }

        public void Configure(EntityRepository world, ModuleHostKernel kernel) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            if (currentTick >= _failAt)
                throw new ScenarioFailureException(1, _message);
            return false;
        }

        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }

    /// <summary>Never returns true — used to trigger timeout.</summary>
    internal sealed class MockNeverSucceedScenario : IScenario
    {
        public string ScenarioName => "mocknever";
        public void Configure(EntityRepository world, ModuleHostKernel kernel) { }
        public bool EvaluateTick(uint currentTick, EntityRepository world) => false;
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }

    /// <summary>
    /// Records <see cref="GlobalTime.DeltaTime"/> from the world singleton each tick.
    /// </summary>
    internal sealed class MockDeltaRecorderScenario : IScenario
    {
        private readonly int _succeedAfterTicks;
        public readonly List<float> RecordedDeltas = new();
        public string ScenarioName => "mockdelta";

        public MockDeltaRecorderScenario(int succeedAfterTicks = 3) => _succeedAfterTicks = succeedAfterTicks;

        public void Configure(EntityRepository world, ModuleHostKernel kernel) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // Reads the GlobalTime singleton set by ScenarioSubsystem before this call.
            var time = world.GetSingletonUnmanaged<GlobalTime>();
            RecordedDeltas.Add(time.DeltaTime);
            return (int)currentTick >= _succeedAfterTicks;
        }

        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }

    /// <summary>
    /// Succeeds at tick 3 and logs a "Phase 1 PASSED" line (for NLog file tests).
    /// </summary>
    internal sealed class MockSampleScenario : IScenario
    {
        public string ScenarioName => "mocksample";

        public void Configure(EntityRepository world, ModuleHostKernel kernel) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            if (currentTick == 3)
                FdpLog<MockSampleScenario>.Info("[mocksample] Phase 1 PASSED tick=3");
            return currentTick >= 3;
        }

        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }

    // ── DEM1-F005: ScenarioTestHarness tests ──────────────────────────────────

    public class ScenarioTestHarnessTests
    {
        [Fact]
        public void ScenarioTestHarness_WithSucceedingScenario_ReturnsZero()
        {
            var scenario = new MockSucceedAtTickScenario(5);
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 20);
            Assert.Equal(0, code);
        }

        [Fact]
        public void ScenarioTestHarness_WithFailingScenario_ReturnsOne()
        {
            var scenario = new MockFailAtTickScenario(3);
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 20);
            Assert.Equal(1, code);
        }

        [Fact]
        public void ScenarioTestHarness_WithTimingOutScenario_ReturnsTwo()
        {
            var scenario = new MockNeverSucceedScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 5);
            Assert.Equal(2, code);
        }
    }

    // ── DEM1-F002: ScenarioSubsystem behaviour tests ──────────────────────────

    public class ScenarioSubsystemTests
    {
        private static MemoryTarget SetupMemoryLog()
        {
            var memTarget = new MemoryTarget("test-mem") { Layout = "${message}" };
            var config    = new LoggingConfiguration();
            config.AddRuleForAllLevels(memTarget);
            LogManager.Configuration = config;
            return memTarget;
        }

        [Fact]
        public void ScenarioSubsystem_ExitsZero_WhenScenarioSucceeds()
        {
            var mem = SetupMemoryLog();

            var scenario = new MockSucceedAtTickScenario(5);
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 20);

            Assert.Equal(0, code);
            Assert.Contains(mem.Logs, l => l.Contains("[CI SUCCESS]"));
        }

        [Fact]
        public void ScenarioSubsystem_ExitsOne_WhenAssertionFails()
        {
            var mem = SetupMemoryLog();

            var scenario = new MockFailAtTickScenario(2, "Y too small");
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 10);

            Assert.Equal(1, code);
            Assert.Contains(mem.Logs, l => l.Contains("[CI FAILURE]"));
            Assert.Contains(mem.Logs, l => l.Contains("Y too small"));
        }

        [Fact]
        public void ScenarioSubsystem_ExitsTwo_OnTimeout()
        {
            var mem = SetupMemoryLog();

            var scenario = new MockNeverSucceedScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 5);

            Assert.Equal(2, code);
            Assert.Contains(mem.Logs, l => l.Contains("[CI TIMEOUT]"));
        }

        [Fact]
        public void ScenarioSubsystem_Deterministic_GlobalTimeHasCorrectDelta()
        {
            const float fixedDt = 0.025f;
            var scenario = new MockDeltaRecorderScenario(succeedAfterTicks: 3);
            ScenarioTestHarness.Run(scenario, maxTicks: 10, dt: fixedDt);

            // Three ticks ran before the success condition (>= 3) was met.
            Assert.Equal(3, scenario.RecordedDeltas.Count);
            foreach (float dt in scenario.RecordedDeltas)
                Assert.Equal(fixedDt, dt, precision: 6);
        }
    }

    // ── DEM1-F004: NLog file output tests ────────────────────────────────────

    public class NLogFileOutputTests : IDisposable
    {
        private readonly string _logDir;
        private readonly LoggingConfiguration _savedConfig;

        public NLogFileOutputTests()
        {
            _logDir = Path.Combine(Path.GetTempPath(), $"fdp-test-logs-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_logDir);
            _savedConfig = LogManager.Configuration;
        }

        public void Dispose()
        {
            LogManager.Configuration = _savedConfig;
            try { Directory.Delete(_logDir, recursive: true); } catch { /* best-effort */ }
        }

        private string SetupFileLog(string scenarioName)
        {
            string ts      = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string logFile = Path.Combine(_logDir, $"demo-{scenarioName}-{ts}.log");

            var fileTarget = new FileTarget("testfile")
            {
                FileName    = logFile,
                Layout      = "${longdate}|${level:uppercase=true}|${logger}|${message}",
                // Use KeepFileOpen=false so the OS file handle is released after each write,
                // allowing File.ReadAllText() to read the log without a sharing violation.
                KeepFileOpen   = false,
                ConcurrentWrites = true,
                AutoFlush      = true
            };
            var cfg = new LoggingConfiguration();
            cfg.AddRuleForAllLevels(fileTarget);
            LogManager.Configuration = cfg;

            return logFile;
        }

        [Fact]
        public void AfterRun_LogFileExists_AndContainsExpectedLines()
        {
            string logFile = SetupFileLog("mocksample");

            var scenario = new MockSampleScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 10);

            // Close NLog so the file handle is released before reading.
            LogManager.Flush();
            LogManager.Configuration = null;

            Assert.Equal(0, code);
            Assert.True(File.Exists(logFile), $"Log file not found: {logFile}");

            string content = File.ReadAllText(logFile);
            Assert.Contains("Phase", content);
            Assert.Contains("PASSED", content);
            Assert.Contains("[CI SUCCESS]", content);
        }

        [Fact]
        public void OnFailure_LogFileContains_DiagnosticValues()
        {
            string logFile = SetupFileLog("mockfail");

            var scenario = new MockFailAtTickScenario(2, "Y=5.3 expected >10");
            ScenarioTestHarness.Run(scenario, maxTicks: 10);

            // Close NLog so the file handle is released before reading.
            LogManager.Flush();
            LogManager.Configuration = null;

            Assert.True(File.Exists(logFile), $"Log file not found: {logFile}");
            string content = File.ReadAllText(logFile);
            Assert.Contains("Y=5.3 expected >10", content);
        }

        [Fact]
        public void PerTickTrace_WritesAtLeastOneTickStatement()
        {
            string logFile = SetupFileLog("mocktrace");

            var scenario = new MockSucceedAtTickScenario(3);
            ScenarioTestHarness.Run(scenario, maxTicks: 10);

            // Close NLog so the file handle is released before reading.
            LogManager.Flush();
            LogManager.Configuration = null;

            Assert.True(File.Exists(logFile), $"Log file not found: {logFile}");
            string content = File.ReadAllText(logFile);
            // The ScenarioSubsystem.Update logs: "[<scenarioName>] tick=<N>" at Trace level each tick.
            Assert.Contains("tick=", content);
        }
    }

    // ── DEM1-F003: Runner integration tests ──────────────────────────────────

    public class RunnerIntegrationTests
    {
        [Fact]
        public void Runner_WithUnknownScenario_ExitsNonZero()
        {
            int capturedCode = -1;
            var stderr = new StringWriter();
            Console.SetError(stderr);

            try
            {
                // Call the testable RunMain with an unknown scenario name.
                Fdp.Examples.Runner.Program.RunMain(
                    args: new[] { "--scenario", "unknown_xyz", "--headless" },
                    stdout: new StringWriter(),
                    exitCallback: code => capturedCode = code);
            }
            finally
            {
                Console.SetError(Console.Error);
            }

            Assert.NotEqual(0, capturedCode);
            string errors = stderr.ToString();
            Assert.Contains("Unknown scenario", errors);
        }

        [Fact]
        public void Runner_PrintsLogFilePath_ToStdout()
        {
            var stdout = new StringWriter();
            int capturedCode = -1;

            Fdp.Examples.Runner.Program.RunMain(
                args: new[] { "--scenario", "placeholder", "--headless", "--deterministic", "--max-ticks", "1" },
                stdout: stdout,
                exitCallback: code => capturedCode = code);

            string output = stdout.ToString();
            // Verify log path line is printed.
            Assert.Contains("[RUNNER] Log:", output);
            // Verify the log path matches the expected naming pattern.
            Assert.Matches(@"\[RUNNER\] Log:.*demo-placeholder.*\.log", output);
        }
    }

    // ── DEM1-D001: AutoDriveScenario tests ───────────────────────────────────

    public class AutoDriveScenarioTests
    {
        /// <summary>
        /// Full scenario run — all 4 phases pass and both vehicles arrive at their
        /// destinations within the 250-tick budget. Exit code must be 0 (CI SUCCESS).
        /// </summary>
        [Fact]
        public void AutoDrive_RunToCompletion_ExitsZero()
        {
            var scenario = new AutoDriveScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 250);
            string diag = $"Exit code {code}. Reason: {scenario.FailureReason ?? "(timeout or unknown)"}. " +
                $"Speed@20={scenario.AlphaSpeedAtTick20:F3} |Y|@70={MathF.Abs(scenario.AlphaYAtTick70):F3} |Y|@160={MathF.Abs(scenario.AlphaYAtTick160):F3}";
            Assert.True(code == 0, diag);
        }

        /// <summary>
        /// By tick 20 both vehicles must have started accelerating (speed &gt; 0) and
        /// must still be close to the X-axis (Y offset &lt; 0.5 m). If this fails,
        /// the navigation command was not received or the kinematics pipeline is broken.
        /// </summary>
        [Fact]
        public void AutoDrive_Phase1_VehiclesAccelerate_ByTick20()
        {
            var scenario = new AutoDriveScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 250);

            // Scenario throws ScenarioFailureException (exit 1) if the Phase 1 check fails.
            // If we reach here with code 0 it means the full scenario succeeded (fine).
            Assert.True(code != 1, $"Exit code 1. Reason: {scenario.FailureReason}. Speed@20={scenario.AlphaSpeedAtTick20:F3} Y@70={scenario.AlphaYAtTick70:F3}");

            // Direct value assertions for independent coverage.
            Assert.True(scenario.AlphaSpeedAtTick20 > 0f,
                $"Alpha speed at tick 20 = {scenario.AlphaSpeedAtTick20:F3} m/s — expected > 0");
        }

        /// <summary>
        /// By tick 70 the RVO solver must have pushed Alpha laterally by more than 2 m,
        /// confirming that collision avoidance activated during the head-on approach.
        /// Modifying AvoidanceRadius to 0 would cause this assertion to fail.
        /// </summary>
        [Fact]
        public void AutoDrive_Phase2_RVOActivates_ByTick70()
        {
            var scenario = new AutoDriveScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 250);

            Assert.NotEqual(1, code);

            Assert.True(MathF.Abs(scenario.AlphaYAtTick70) > 2.0f,
                $"|Alpha.Y| at tick 70 = {MathF.Abs(scenario.AlphaYAtTick70):F3} m — expected > 2.0 m");
        }

        /// <summary>
        /// Both vehicles must arrive at their destinations within 200 ticks.
        /// The full scenario run (maxTicks=250) exercises phase 4 directly —
        /// if arrivals are slow or blocked the scenario throws at tick > 200.
        /// </summary>
        [Fact]
        public void AutoDrive_Phase4_BothVehiclesArrive_WithinBudget()
        {
            var scenario = new AutoDriveScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 250);

            Assert.Equal(0, code);
            Assert.True(scenario.BothArrivedByTick200,
                "Both vehicles must have arrived at their destinations by tick 200.");
        }
    }

    // ── DEM1-D002: ComponentDamageScenario tests ─────────────────────────────

    public class ComponentDamageScenarioTests
    {
        /// <summary>
        /// Full scenario run — all 5 phases pass and exit code is 0 (CI SUCCESS).
        /// The APC receives a non-lethal hit, loses mobility but retains firepower.
        /// </summary>
        [Fact]
        public void ComponentDamage_RunToCompletion_ExitsZero()
        {
            int code = ScenarioTestHarness.Run(new ComponentDamageScenario(), maxTicks: 60);
            Assert.Equal(0, code);
        }

        /// <summary>
        /// After the HitEvent at tick 20 is processed by DamageSystem, the APC's health
        /// must be below its maximum value by tick 21. Changing HitDamage to 0 would
        /// cause this test to fail.
        /// </summary>
        [Fact]
        public void ComponentDamage_Phase2_HealthDecreases_AfterHit()
        {
            var scenario = new ComponentDamageScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.NotEqual(1, code);

            Assert.True(scenario.HealthAfterHit < scenario.HealthAtBaseline,
                $"Health after hit ({scenario.HealthAfterHit}) must be less than baseline ({scenario.HealthAtBaseline})");
        }

        /// <summary>
        /// The MobilityKillSystem strips CanMove from the APC on tick 22 (first frame
        /// after damage is applied). Removing or breaking that system causes this to fail.
        /// </summary>
        [Fact]
        public void ComponentDamage_Phase3_MoveFlagStripped_AfterDamage()
        {
            var scenario = new ComponentDamageScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.NotEqual(1, code);

            Assert.False(scenario.CanMoveAtTick22,
                "CanMove flag must be stripped at tick 22 after the mobility kill hit.");
        }

        /// <summary>
        /// After mobility is killed (CanMove stripped), the LocomotionClearOnMobilityKillSystem
        /// (HSM bridge response) must zero out LocomotionChannel.ActiveAction by tick 25.
        /// Removing that system causes this assertion to fail.
        /// </summary>
        [Fact]
        public void ComponentDamage_Phase4_LocomotionCleared_ByHSM()
        {
            var scenario = new ComponentDamageScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.NotEqual(1, code);

            Assert.Equal(0, (int)scenario.LocoActionAtTick25);
        }

        /// <summary>
        /// WeaponChannel.ActiveAction must still equal <see cref="CombatConstants.ActionIdAimAndFire"/>
        /// at tick 45, confirming that mobility kill does NOT strip firepower.
        /// Changing combat constants or accidentally clearing WeaponChannel would fail this test.
        /// </summary>
        [Fact]
        public void ComponentDamage_Phase5_WeaponStillFires_AfterMobilityKill()
        {
            var scenario = new ComponentDamageScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.Equal(0, code);

            Assert.Equal(CombatConstants.ActionIdAimAndFire, scenario.WeaponActionAtTick45);
        }
    }
}
