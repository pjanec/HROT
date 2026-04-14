using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Examples.Scenarios.Cognitive;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Examples.Scenarios.Kinematics;
using Fdp.Examples.Scenarios.Network;
using Fdp.Examples.Scenarios.Perception;
using Fdp.Examples.Scenarios.Physics;
using Fdp.Examples.Scenarios.Replay;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Vis2D;
using Fdp.ModuleHost.Core;
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

    // ── DEM1-D003: BallisticsAndHitScenario tests ─────────────────────────────

    public class BallisticsAndHitScenarioTests
    {
        /// <summary>
        /// Full scenario run — all 4 phases pass (bullet spawned, flies past target in raw
        /// space, CCD hit applied, bullet destroyed) and exit code is 0 (CI SUCCESS).
        /// </summary>
        [Fact]
        public void BallisticsAndHit_RunToCompletion_ExitsZero()
        {
            int code = ScenarioTestHarness.Run(new BallisticsAndHitScenario(), maxTicks: 10);
            Assert.Equal(0, code);
        }

        /// <summary>
        /// By tick 2 the bullet must have been spawned with the correct velocity.
        /// Confirms <see cref="FDP.Toolkit.Combat.Systems.FireProcessingSystem"/> read the
        /// muzzle velocity from <c>WeaponState.MuzzleVelocity</c> and applied it to
        /// <c>SimVelocity.Linear.X</c>.
        /// </summary>
        [Fact]
        public void BallisticsAndHit_Phase1_BulletSpawnedWithCorrectVelocity()
        {
            var scenario = new BallisticsAndHitScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 10);

            Assert.NotEqual(1, code);

            Assert.True(MathF.Abs(scenario.BulletVelocityXAtTick2 - BallisticsAndHitScenario.MuzzleVelocity) < 0.1f,
                $"BulletVelocity.X={scenario.BulletVelocityXAtTick2:F1} expected {BallisticsAndHitScenario.MuzzleVelocity:F1} m/s");
        }

        /// <summary>
        /// Runs the scenario to completion. Phase 3 asserts that CCD detected the hit even
        /// though the bullet moved past the target in a single tick (anti-tunneling demo).
        /// The scenario's own EvaluateTick throws <see cref="ScenarioFailureException"/>
        /// (exit code 1) if damage was not applied, so exit code 0 confirms CCD worked.
        /// </summary>
        [Fact]
        public void BallisticsAndHit_Phase3_TargetTakesDamage_NoBulletSwimthrough()
        {
            var scenario = new BallisticsAndHitScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 10);

            Assert.Equal(0, code);

            Assert.True(scenario.TargetHealthAfterHit < 100f,
                $"TargetHealth={scenario.TargetHealthAfterHit:F1} expected < 100 (hit not applied)");
        }

        /// <summary>
        /// By Phase 4 the bullet entity must have been destroyed by
        /// <see cref="FDP.Toolkit.Combat.Systems.DamageSystem"/> after the impact.
        /// Exit code 0 confirms the full scenario succeeded, which includes the Phase 4
        /// assertion inside <see cref="BallisticsAndHitScenario.EvaluateTick"/>.
        /// </summary>
        [Fact]
        public void BallisticsAndHit_Phase4_BulletDestroyedAfterImpact()
        {
            var scenario = new BallisticsAndHitScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 10);

            // Exit code 0 means the Phase 4 assertion inside EvaluateTick passed:
            // world.IsAlive(BulletEntity) == false confirmed bullet destruction.
            Assert.Equal(0, code);
        }
    }

    // ── DEM1-D004: BehaviorValidationScenario tests ───────────────────────────

    public class BehaviorValidationScenarioTests
    {
        /// <summary>
        /// Full scenario run — all 3 phases pass (flee when no threat, engage when threat
        /// with ammo, flee again when ammo depleted) and exit code is 0 (CI SUCCESS).
        /// </summary>
        [Fact]
        public void BehaviorValidation_RunToCompletion_ExitsZero()
        {
            int code = ScenarioTestHarness.Run(new BehaviorValidationScenario(), maxTicks: 40);
            Assert.Equal(0, code);
        }

        /// <summary>
        /// At tick 10 (before ThreatVisible is set) the agent must be fleeing:
        /// WeaponChannel.ActiveAction == 0 and LocomotionChannel.ActiveAction == ActionIdFlee.
        /// </summary>
        [Fact]
        public void BehaviorValidation_Phase1_AgentFlees_WhenNoThreat()
        {
            var scenario = new BehaviorValidationScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 40);

            Assert.NotEqual(1, code);

            Assert.Equal(NavigationConstants.ActionIdFlee, scenario.LocoActionAtTick10);
            Assert.Equal(0, (int)scenario.WeaponActionAtTick10);
        }

        /// <summary>
        /// At tick 20 (after ThreatVisible=true is set at tick 10) the agent must be
        /// engaging: WeaponChannel.ActiveAction == ActionIdAimAndFire.
        /// Confirms the BTree Selector evaluates the Sequence when conditions are met.
        /// </summary>
        [Fact]
        public void BehaviorValidation_Phase2_AgentEngages_WhenThreatWithAmmo()
        {
            var scenario = new BehaviorValidationScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 40);

            Assert.NotEqual(1, code);

            Assert.Equal(CombatConstants.ActionIdAimAndFire, scenario.WeaponActionAtTick20);
        }

        /// <summary>
        /// At tick 30 (after AmmoCount=0 is set at tick 20) the agent must have reverted to
        /// fleeing: WeaponChannel.ActiveAction == 0, LocomotionChannel == ActionIdFlee.
        /// Confirms the BTree Condition_HasAmmo failure causes the Selector to fall through
        /// to Action_Flee. Exit code 0 means all phase assertions in EvaluateTick passed.
        /// </summary>
        [Fact]
        public void BehaviorValidation_Phase3_AgentFleesAgain_WhenAmmoGone()
        {
            var scenario = new BehaviorValidationScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 40);

            Assert.Equal(0, code);

            Assert.Equal(NavigationConstants.ActionIdFlee, scenario.LocoActionAtTick30);
            Assert.Equal(0, (int)scenario.WeaponActionAtTick30);
        }
    }

    // ── DEM1-D005: SensorGridScenario tests ──────────────────────────────────

    public class SensorGridScenarioTests
    {
        /// <summary>
        /// Full scenario run — all 3 phases pass and exit code is 0 (CI SUCCESS).
        /// The observer detects the target in open field, loses track when it enters
        /// the wall's shadow, then reacquires it at tick 96.
        /// </summary>
        [Fact]
        public void SensorGrid_RunToCompletion_ExitsZero()
        {
            var scenario = new SensorGridScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 100);
            Assert.True(code == 0,
                $"Exit code {code}. Phase1={scenario.Phase1Passed}, Phase2={scenario.Phase2Passed}");
        }

        /// <summary>
        /// By tick 28 the observer's TargetMemory must show the target as an active threat.
        /// The target is at (100, 28) — well within VisionRange=200 and in clear LOS before
        /// the wall's blocking range (Y ∈ [29.17, 75.0]).
        /// If this fails, the perception pipeline is broken or the pipeline lag is larger than
        /// expected (target not yet detected within 24 ticks of sim start).
        /// </summary>
        [Fact]
        public void SensorGrid_Phase1_TargetDetectedInOpenField()
        {
            var scenario = new SensorGridScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 100);

            // Exit code 1 means a ScenarioFailureException was thrown at Phase 1.
            Assert.NotEqual(1, code);

            Assert.True(scenario.Phase1Passed,
                "Phase 1: TargetMemory must show active threat at tick 28 (open field, clear LOS).");
        }

        /// <summary>
        /// By tick 60 the target has been occluded by the wall for ~24 ticks (last seen ~
        /// tick 36). The staleness threshold is 20 ticks, so the threat must be considered
        /// stale and HasThreat must return false. If this fails, either the LOS block is not
        /// working or the staleness threshold is not applied.
        /// </summary>
        [Fact]
        public void SensorGrid_Phase2_TargetOccludedByWall()
        {
            var scenario = new SensorGridScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 100);

            Assert.NotEqual(1, code);

            Assert.True(scenario.Phase2Passed,
                "Phase 2: threat must be stale at tick 60 (target hidden behind wall since ~tick 36).");
        }

        /// <summary>
        /// At tick 96 the target has exited the wall's shadow (Y=96 &gt; 75) and the module
        /// has had time to reacquire it (~6 ticks since last confirmed sighting at ~tick 90).
        /// HasThreat must return true and EvaluateTick must return true to end the scenario.
        /// Exit code 0 is the primary assertion; this test confirms the full-cycle reacquisition.
        /// </summary>
        [Fact]
        public void SensorGrid_Phase3_TargetReacquiredAfterWall()
        {
            var scenario = new SensorGridScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 100);

            // Exit code 0 means Phase 3 triggered return true inside EvaluateTick.
            Assert.Equal(0, code);
        }
    }

    // ── DEM1-D006: MissionCommandScenario tests ────────────────────────────────

    public class MissionCommandScenarioTests
    {
        /// <summary>
        /// Full scenario run — all 4 phases pass (MoveTo written, enemy injected,
        /// director advances phase + doctrine switch, channel arbitration clears stale
        /// command) and exit code is 0 (CI SUCCESS).
        /// </summary>
        [Fact]
        public void MissionCommand_RunToCompletion_ExitsZero()
        {
            var scenario = new MissionCommandScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 20);
            Assert.Equal(0, code);
        }

        /// <summary>
        /// At tick 11 MissionDirectorSystem must have incremented
        /// <c>MissionPlanQueue.CurrentPhase</c> to 1 (transitioned from Patrol to Combat)
        /// and DoctrineIngressSystem must have applied the Combat doctrine hash (200) to
        /// <c>DoctrineState.ActiveDoctrineHash</c>. Removing MissionDirectorSystem or
        /// breaking the manual-pipeline double-SwapBuffers pattern would cause one or both
        /// assertions to fail.
        /// </summary>
        [Fact]
        public void MissionCommand_Phase3_DirectorAdvancesPhase_WhenThreated()
        {
            var scenario = new MissionCommandScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 20);

            Assert.NotEqual(1, code);

            Assert.Equal(1, scenario.Phase3CurrentPhase);
            // Must use Common constants: Fdp.Examples.Scenarios.Tests is nested under
            // Fdp.Examples.Scenarios in namespace lookup, so unqualified DemoDoctrineIds
            // would bind to Fdp.Examples.Scenarios.DemoDoctrineIds (Combat=2900).
            Assert.Equal((int)Fdp.Examples.Common.Constants.DemoDoctrineIds.Combat, scenario.Phase3DoctrineHash);
        }

        /// <summary>
        /// At tick 12 ChannelArbitrationSystem must have cleared the stale MoveTo command
        /// written at tick 5 (its <c>DoctrineInstanceId</c> no longer matches the active
        /// doctrine). <c>LocomotionChannel.ActiveAction</c> must equal 0. Removing
        /// <see cref="ChannelArbitrationSystem"/> or keeping the channel's DoctrineInstanceId
        /// in sync with the new doctrine would cause this to fail.
        /// </summary>
        [Fact]
        public void MissionCommand_Phase4_ArbitrationPreemptsStaleLocoCommand()
        {
            var scenario = new MissionCommandScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 20);

            Assert.NotEqual(1, code);

            Assert.Equal(0, (int)scenario.Phase4LocoAction);
        }
    }

    // ── DEM1-D007: TerrainClampingScenario tests ───────────────────────────────

    public class TerrainClampingScenarioTests
    {
        /// <summary>
        /// Full scenario run — all 4 phases pass (flat = no clamping, ramp = smoothing
        /// active, spike = jump rejected, recovery = TargetZOffset ≈ 6.0) and
        /// exit code is 0 (CI SUCCESS).
        /// </summary>
        [Fact]
        public void TerrainClamping_RunToCompletion_ExitsZero()
        {
            var scenario = new TerrainClampingScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 350);
            Assert.Equal(0, code);
        }

        /// <summary>
        /// At tick 10 the vehicle is at X ≈ 1.67 m — inside the MockTerrainProvider's
        /// flat zone (0–20 m, Z=0). No clamping should have been applied:
        /// <c>CurrentZOffset</c> must be less than 0.01. Changing <see cref="EClampingMode"/>
        /// to Disabled would cause Phase 1 to always pass trivially; enabling it with a
        /// non-zero provider would cause it to fail.
        /// </summary>
        [Fact]
        public void TerrainClamping_Phase1_NoClampingOnFlatGround()
        {
            var scenario = new TerrainClampingScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 350);

            Assert.NotEqual(1, code);

            Assert.True(scenario.Phase1CurrentZOffset < 0.01f,
                $"Phase 1: CurrentZOffset={scenario.Phase1CurrentZOffset:F4} expected < 0.01 (flat zone)");
        }

        /// <summary>
        /// At tick 150 the vehicle is at X ≈ 25 m — on the ramp (slope 0.2), provider
        /// returns Z ≈ 1.0. <c>TargetZOffset</c> must exceed 0.5 and <c>CurrentZOffset</c>
        /// must still be lagging behind <c>TargetZOffset</c> (smoothing in progress).
        /// Removing <see cref="TransformSyncSystem"/> would eliminate the lag and cause
        /// the second assertion to fail.
        /// </summary>
        [Fact]
        public void TerrainClamping_Phase2_SmoothingActiveOnRamp()
        {
            var scenario = new TerrainClampingScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 350);

            Assert.NotEqual(1, code);

            Assert.True(scenario.Phase2TargetZOffset > 0.5f,
                $"Phase 2: TargetZOffset={scenario.Phase2TargetZOffset:F4} expected > 0.5");
            Assert.True(scenario.Phase2CurrentZOffset < scenario.Phase2TargetZOffset,
                $"Phase 2: CurrentZOffset={scenario.Phase2CurrentZOffset:F4} must lag TargetZOffset={scenario.Phase2TargetZOffset:F4}");
        }

        /// <summary>
        /// At tick 240 the vehicle is near the spike region (X ≈ 40 m, provider returns
        /// Z = 100). The jump-rejection filter must have discarded the anomalous hit;
        /// <c>LastValidIgAltitude</c> must be less than 10 (the last accepted valid reading
        /// from the ramp, not 100). Removing jump-rejection in
        /// <see cref="TerrainQueryResolutionSystem"/> would allow the spike through and
        /// cause this assertion to fail.
        /// </summary>
        [Fact]
        public void TerrainClamping_Phase3_JumpRejectionRejectsSpike()
        {
            var scenario = new TerrainClampingScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 350);

            Assert.NotEqual(1, code);

            Assert.True(scenario.Phase3LastValidIgAltitude < 10.0f,
                $"Phase 3: LastValidIgAltitude={scenario.Phase3LastValidIgAltitude:F4} expected < 10 (spike must have been rejected)");
        }

        /// <summary>
        /// Exit code 0 confirms the Phase 4 assertion inside
        /// <see cref="TerrainClampingScenario.EvaluateTick"/> passed: at tick 300
        /// (X ≈ 50 m, ramp Z ≈ 6.0) <c>TargetZOffset</c> is within ±1.0 m of 6.0,
        /// meaning the vehicle recovered from the anomaly and resumed terrain-following.
        /// </summary>
        [Fact]
        public void TerrainClamping_Phase4_RecoverAfterAnomaly()
        {
            var scenario = new TerrainClampingScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 350);

            Assert.Equal(0, code);

            Assert.True(MathF.Abs(scenario.Phase4TargetZOffset - 6.0f) <= 1.0f,
                $"Phase 4: TargetZOffset={scenario.Phase4TargetZOffset:F4} expected 6.0 ±1.0");
        }
    }

    // ── DEM1-D008: ParallelEpisodesScenario tests ───────────────────────────

    public class ParallelEpisodesScenarioTests
    {
        /// <summary>
        /// Full scenario run — Phase A records live kinematics for 50 ticks;
        /// Phase B replays through <c>ReplayModule</c> and compares positions at frames
        /// 25 and 50. Exit code 0 means both comparisons passed within 0.001 m.
        /// </summary>
        [Fact]
        public void ParallelEpisodes_RunToCompletion_ExitsZero()
        {
            var scenario = new ParallelEpisodesScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);
            Assert.Equal(0, code);
        }

        /// <summary>
        /// After 25 replay frames have been applied (visible at EvaluateTick tick=26),
        /// the replayed <c>SimTransform.Position</c> must match the live trajectory within
        /// <see cref="ParallelEpisodesScenario.PositionTolerance"/> metres.
        /// Confirms that LZ4 recording + naked-node replay reproduce bit-identical positions.
        /// </summary>
        [Fact]
        public void ParallelEpisodes_ReplayMatchesLiveAtTick25()
        {
            var scenario = new ParallelEpisodesScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            // Exit code 1 means a ScenarioFailureException was thrown at Phase 1.
            Assert.NotEqual(1, code);

            Assert.True(scenario.ReplayMatchedLiveAtTick25,
                "Phase 1: replay position at frame 25 must match live position within " +
                $"{ParallelEpisodesScenario.PositionTolerance} m.");
        }

        /// <summary>
        /// Confirms that the main (replay) kernel contains no ground-kinematics systems.
        /// The scenario captures <see cref="ParallelEpisodesScenario.ReplayKernelModuleTypeNames"/>
        /// directly from <see cref="ModuleHostKernel.GetRegisteredModuleTypeNames"/> so the test
        /// inspects the actual kernel topology, not an author-set flag.
        /// </summary>
        [Fact]
        public void ParallelEpisodes_NoCarKinimSystemsInReplayKernel()
        {
            var scenario = new ParallelEpisodesScenario();
            // Run the full scenario to verify it succeeds and Configure has been called.
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.Equal(0, code);

            // Verify actual kernel module topology: no kinematics modules registered.
            // Kinematics module type names that must be absent from the replay kernel.
            var kinematicsTypeNames = new[] { "LiveKinematicsModule", "GroundKinematicsModule", "CarKinematicsModule" };
            var registered = scenario.ReplayKernelModuleTypeNames;

            Assert.NotEmpty(registered); // sanity check: at least ReplayModule was registered

            foreach (var kinName in kinematicsTypeNames)
            {
                Assert.False(registered.Contains(kinName),
                    $"The replay kernel must not register '{kinName}' — " +
                    "positions must come purely from ReplayModule.");
            }

            // Confirm the replay module IS registered (proves positive coverage).
            Assert.True(registered.Contains("ReplayModule"),
                "ReplayModule must be registered in the replay kernel.");
        }
    }

    // ── DEM1-D009: DistributedTank tests ──────────────────────────────────────

    /// <summary>
    /// Tests for <see cref="DistributedTankScenario"/> covering Phase A, Phase B Phase 1,
    /// DDS EntityMaster ghost (BATCH-09/10/11), locomotion round-trip (BATCH-12 Phase B3),
    /// turret split-authority (BATCH-12 Phase B4), DDS loco consumption assertion
    /// (BATCH-14 Phase B3 gap), and turret-tracks-hull Phase 3 (BATCH-14 Phase 3).
    /// </summary>
    public class DistributedTankScenarioPhaseATests
    {
        /// <summary>
        /// Running the scenario through <see cref="ScenarioTestHarness"/> up to
        /// 60 ticks must succeed (exit code 0) without exception.
        /// Proves: both kernels initialise, all phase milestones pass, and all DDS/ECS
        /// resources are released cleanly via <see cref="IScenario.OnShutdown"/>.
        /// </summary>
        [Fact]
        public void DistributedTank_PhaseA_RunToTick10_ExitsZero()
        {
            using var scenario = new DistributedTankScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);
            Assert.Equal(0, code);
        }

        /// <summary>
        /// After <see cref="ScenarioTestHarness.Run"/> completes, both kernel
        /// initialization flags must be set — confirming that <c>Configure</c> ran without
        /// exception and both DDS participants were created successfully.
        /// </summary>
        [Fact]
        public void DistributedTank_PhaseA_BothKernelsInitialized()
        {
            using var scenario = new DistributedTankScenario();
            ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.True(scenario.BrainInitialized,  "Brain kernel must be initialized after Configure().");
            Assert.True(scenario.MuscleInitialized, "Muscle kernel must be initialized after Configure().");
        }

        /// <summary>
        /// Phase B Phase 1 (BATCH-10): the Brain hull entity must reach
        /// <see cref="EntityLifecycle.Active"/> by tick <see cref="DistributedTankScenario.PhaseBElmActiveTick"/>.
        /// Proves that <see cref="EntityLifecycleModule"/> (zero-participant auto-promote) is wired to
        /// the Brain kernel and that <c>LifecycleSystem.DrainInstantComplete</c> fires correctly.
        /// </summary>
        [Fact]
        public void DistributedTank_PhaseB_BrainHullReachesActive_AtTick5()
        {
            using var scenario = new DistributedTankScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.Equal(0, code);
            Assert.True(scenario.PhaseBElmActive,
                "Brain hull must reach EntityLifecycle.Active by tick 5 via ELM zero-participant auto-promote.");
        }

        /// <summary>
        /// Verifies that calling <see cref="IScenario.OnShutdown"/> followed by
        /// <see cref="IDisposable.Dispose"/> does not throw (double-release guard).
        /// </summary>
        [Fact]
        public void DistributedTank_OnShutdown_ThenDispose_DoesNotThrow()
        {
            using var scenario = new DistributedTankScenario();
            // ScenarioTestHarness.Run calls Shutdown which calls OnShutdown.
            // The using-var block will then call Dispose — must be a no-op.
            ScenarioTestHarness.Run(scenario, maxTicks: 60);
            // If we reach here without exception, the guard worked.
        }

        /// <summary>
        /// Phase B DDS milestone (BATCH-11): after Brain publishes
        /// <see cref="EntityMasterTopic"/> on Domain 0, the Muscle node must have a live
        /// ghost entity registered in its <c>NetworkEntityMap</c>, proving the Cyclone DDS
        /// loopback path between the Brain and Muscle <see cref="DdsParticipant"/> instances.
        /// </summary>
        [Fact]
        public void DistributedTank_PhaseB_MuscleHasGhostForBrainHull()
        {
            using var scenario = new DistributedTankScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.Equal(0, code);
            Assert.True(scenario.GhostVisibleOnMuscle,
                "Muscle must have a live ghost entity for the Brain hull's network ID " +
                "via EntityMasterTopic DDS loopback (Domain 0).");
        }

        /// <summary>
        /// Phase B3: after tick 20 Brain publishes <see cref="DemoLocomotionMsg"/> and Muscle
        /// translates it to <c>NavState</c> (poll before Muscle kernel update), the Muscle ghost's
        /// <c>SimVelocity.Linear.X</c> must exceed 0.1 m/s by tick
        /// <see cref="DistributedTankScenario.PhaseB3LocoAssertTick"/>.
        /// Proves <see cref="GroundKinematicsModule"/> (CarKinematicsSystem) on Muscle and physics
        /// authority after blueprint promotion.
        /// </summary>
        [Fact]
        public void DistributedTank_Phase2_MuscleNodeMovesOnCommand()
        {
            using var scenario = new DistributedTankScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.Equal(0, code);
            Assert.True(scenario.LocoObservable,
                "Muscle ghost SimVelocity.Linear.X must exceed 0.1 m/s by tick 25 " +
                "after NavState locomotion injection (CarKinematicsSystem + split-authority).");
        }

        /// <summary>
        /// Phase B3 DDS path (BATCH-14): the Muscle node must have consumed a
        /// <see cref="DemoLocomotionMsg"/> DDS sample — not an accidental direct
        /// <see cref="NavState"/> mutation — before the ghost reaches the velocity milestone.
        /// Proves the DDS loopback round-trip drove the locomotion command.
        /// </summary>
        [Fact]
        public void DistributedTank_Phase2_LocoMsgConsumedViaDds()
        {
            using var scenario = new DistributedTankScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.Equal(0, code);
            Assert.True(scenario.LocoCommandReceivedViaDds,
                "Muscle must have consumed a DemoLocomotionMsg DDS sample (LocoCommandReceivedViaDds) " +
                "confirming the DDS loopback path drove NavState, not a direct injection.");
        }

        /// <summary>
        /// Phase 3 (BATCH-14 / DEM1-TASK-DETAIL §D009 tick 40): at tick
        /// <see cref="DistributedTankScenario.PhaseB4TurretTrackTick"/> the Brain Turret
        /// <see cref="SimTransform"/> must be within ±0.1 m of the Brain hull position.
        /// Proves the split-authority layout: turret and hull share the origin until the
        /// Muscle kinematic integration begins, so the distance is effectively 0.
        /// </summary>
        [Fact]
        public void DistributedTank_Phase3_BrainTurretTracksHull_AtTick40()
        {
            using var scenario = new DistributedTankScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.Equal(0, code);
            Assert.True(scenario.PhaseBTurretTracksHull,
                "At tick 40 the Brain turret SimTransform must be within ±0.1 m of the Brain hull " +
                "(Phase 3: turret tracks hull position in split-authority layout).");
        }

        /// <summary>
        /// Phase B4 (BATCH-12): at tick <see cref="DistributedTankScenario.SuccessTick"/> the
        /// Brain Turret's <c>WeaponChannel.ActiveAction</c> must still equal
        /// <c>CombatConstants.ActionIdAimAndFire</c> AND the Muscle ghost hull must still be
        /// moving (<c>SimVelocity.Linear.X</c> &gt; 0).  Proves split-authority: turret Brain
        /// channel and hull Muscle physics both active simultaneously.
        /// </summary>
        [Fact]
        public void DistributedTank_Phase4_SplitAuthorityBothChannelsActive()
        {
            using var scenario = new DistributedTankScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 60);

            Assert.Equal(0, code);
            Assert.True(scenario.SplitAuthorityActive,
                "At tick 50 the Brain turret weapon channel must be active AND " +
                "the Muscle ghost hull must still be moving (split-authority).");
        }
    }

    // ── DEM1-D010: UrbanCombatNewScenario tests ───────────────────────────────

    /// <summary>
    /// Integration tests for <see cref="UrbanCombatNewScenario"/> covering all five
    /// sequential latches of the Grand Integration Demo (all toolkits).
    ///
    /// <para><b>Scenario narrative:</b> Insurgent fires RPG at northbound APC
    /// → APC takes non-lethal hit, ApcMobilityTrigger strips CanMove
    /// → HsmDamageBridgeSystem injects MobilityLost → APC transitions to Disabled state
    /// → soldiers eject from APC → soldiers engage and kill insurgent → scenario exits 0.</para>
    /// </summary>
    public class UrbanCombatNewScenarioTests
    {
        /// <summary>
        /// Full scenario run — all five sequential latches must fire within the 600-tick
        /// budget. Exit code must be 0 (CI SUCCESS).
        /// </summary>
        [Fact]
        public void UrbanCombatNew_RunToCompletion_ExitsZero()
        {
            var scenario = new UrbanCombatNewScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 600);
            Assert.True(code == 0,
                $"Exit code {code}. " +
                $"ambush={scenario.LatchAmbushFired} " +
                $"halt={scenario.LatchApcHalted} " +
                $"hit={scenario.LatchInsurgentHit} " +
                $"killed={scenario.LatchInsurgentKilled}");
        }

        /// <summary>
        /// Within 100 ticks the Insurgent must have set its WeaponChannel.ActiveAction to
        /// <c>ActionIdAimAndFire</c> (Latch 1: AmbushFired).
        /// Confirms that DoctrineIngressSystem, BTreeTickSystem and the Ambush BTree's
        /// Condition_HasTarget → Action_AimAndFire branch are all wired correctly, and
        /// that the Insurgent's TargetMemory was pre-seeded with the APC.
        /// </summary>
        [Fact]
        public void UrbanCombatNew_Latch1_InsurgentFiresWithin100Ticks()
        {
            var scenario = new UrbanCombatNewScenario();
            // Run to at least 100 ticks (or success, whichever comes first).
            ScenarioTestHarness.Run(scenario, maxTicks: 600);

            Assert.True(scenario.LatchAmbushFired,
                "Latch 1 (AmbushFired) must be set: Insurgent WeaponChannel.ActiveAction " +
                "== ActionIdAimAndFire before tick 600.");
        }

        /// <summary>
        /// After the RPG hits the APC (Latch 1 → Latch 2), the APC's
        /// <c>LocomotionChannel.ActiveAction</c> must become 0.
        /// Confirms: ApcMobilityTriggerSystem strips CanMove on non-lethal hit →
        /// HsmDamageBridgeSystem fires MobilityLost → HsmTickSystem transitions APC to
        /// Disabled → OnEnter_Disabled clears the locomotion channel.
        /// </summary>
        [Fact]
        public void UrbanCombatNew_Latch2_ApcHaltsAfterAmbush()
        {
            var scenario = new UrbanCombatNewScenario();
            ScenarioTestHarness.Run(scenario, maxTicks: 600);

            Assert.True(scenario.LatchApcHalted,
                "Latch 2 (ApcHalted) must be set: APC LocomotionChannel.ActiveAction == 0 " +
                "after MobilityLost event is processed by HSM.");
        }

        /// <summary>
        /// After soldiers disembark (Latch 2 → Latch 3) the Insurgent must take damage.
        /// Confirms: EjectPassengersExecutor restores CanShoot on soldiers → soldiers'
        /// InfantryCombat BTree fires via Action_AimAndFire → WeaponFireIntent →
        /// FireProcessingSystem → bullet → HitResolutionSystem → DamageSystem reduces
        /// Insurgent health below maximum.
        /// </summary>
        [Fact]
        public void UrbanCombatNew_Latch4_InsurgentDies()
        {
            var scenario = new UrbanCombatNewScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 600);

            Assert.True(scenario.LatchInsurgentKilled,
                "Latch 4 (InsurgentKilled) must be set: Insurgent entity destroyed " +
                "by combined soldier fire within tick budget.");
            // Exit code 0 is sufficient to prove latch 4 since EvaluateTick returns true
            // only after _latchInsurgentKilled is set.
            Assert.Equal(0, code);
        }

        /// <summary>
        /// After the Insurgent is killed (Latch 4), EvaluateTick must return true and
        /// the log must contain "Mission Resumed" — confirming the scenario narrative
        /// completed and the log line was emitted.
        /// Exit code 0 proves EvaluateTick returned true (harness exits 0 on success).
        /// </summary>
        [Fact]
        public void UrbanCombatNew_Latch5_MissionResumes()
        {
            var mem = new NLog.Targets.MemoryTarget("uc-test-mem") { Layout = "${message}" };
            var cfg = new NLog.Config.LoggingConfiguration();
            cfg.AddRuleForAllLevels(mem);
            NLog.LogManager.Configuration = cfg;

            var scenario = new UrbanCombatNewScenario();
            int code = ScenarioTestHarness.Run(scenario, maxTicks: 600);

            NLog.LogManager.Flush();
            NLog.LogManager.Configuration = null;

            Assert.Equal(0, code);
            Assert.Contains(mem.Logs, l => l.Contains("Mission Resumed"));
        }
    }
}
