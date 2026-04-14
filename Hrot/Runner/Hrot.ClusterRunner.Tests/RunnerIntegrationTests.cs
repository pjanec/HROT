using System;
using System.IO;
using System.Threading.Tasks;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Services;
using Hrot.ClusterRunner.Tests.Mocks;
using Fdp.Kernel;
using Microsoft.Extensions.Logging;
using Xunit;
using RunnerConfiguration = Hrot.ClusterRunner.Configuration.HrotRunnerConfiguration;

namespace Hrot.ClusterRunner.Tests
{
    /// <summary>
    /// Phase R4 integration tests.
    ///
    /// <para>These tests prove the core value proposition of <see cref="SubsystemOrchestrator"/>:
    /// all three subsystems can be embedded and managed inside a single process
    /// (<c>--mode all</c>), and the <see cref="HeadlessTestExecutor"/> can
    /// run a complete test script end-to-end.</para>
    /// </summary>
    public class RunnerAggregatedModeTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static RunnerConfiguration HeadlessAllNoWait()
        {
            var cfg = new RunnerConfiguration
            {
                ModeString = "all",
                Headless   = true,
                NoWait     = true,
            };
            cfg.Validate();
            return cfg;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Booting with <c>--mode all</c> and three injected mock subsystems
        /// must call <c>Initialize</c> on every subsystem.
        /// </summary>
        [Fact]
        public void AggregatedMode_AllThreeSubsystems_InitializeSuccessfully()
        {
            // Arrange — one mock per logical subsystem
            var simHost = new MockSubsystem("SimHost");
            var ig      = new MockSubsystem("IG");
            var ios     = new MockSubsystem("ExCon");

            var orchestrator = new SubsystemOrchestrator(
                new ISubsystem[] { simHost, ig, ios },
                new RunnerOptions { Headless = true });

            // Act
            orchestrator.Initialize();

            // Assert — all three subsystems were initialised
            Assert.True(simHost.InitializeCalled, "SimHost subsystem should be initialized");
            Assert.True(ig.InitializeCalled,      "IG subsystem should be initialized");
            Assert.True(ios.InitializeCalled,     "ExCon subsystem should be initialized");

            orchestrator.Shutdown();
        }

        /// <summary>
        /// In <c>--mode all</c> the <see cref="RunnerConfiguration.RequestedSubsystems"/>
        /// must contain all three subsystem names, proving a single process hosts every
        /// subsystem without conditional omissions.
        /// </summary>
        [Fact]
        public void AggregatedMode_ParsedMode_ContainsAllSubsystemFlags()
        {
            var cfg = HeadlessAllNoWait();

            Assert.True(cfg.RequestedSubsystems.Contains("simhost"),      "mode all must include simhost");
            Assert.True(cfg.RequestedSubsystems.Contains("ig"),           "mode all must include ig");
            Assert.True(cfg.RequestedSubsystems.Contains("excon"),        "mode all must include excon");
            Assert.True(cfg.RequestedSubsystems.Contains("orchestrator"), "mode all must include orchestrator");
            Assert.True(cfg.RequestedSubsystems.Contains("cgf"),          "mode all must include cgf");
        }

        /// <summary>
        /// When <c>--no-wait</c> is set (all subsystems co-located), the
        /// <see cref="WaitingRoomCoordinator"/> wait is bypassed: <c>WaitForPeers</c>
        /// must be empty so no DDS blocking poll is performed.
        /// </summary>
        [Fact]
        public void AggregatedMode_NoWait_WaitingRoomBypassed()
        {
            var cfg = HeadlessAllNoWait();

            // Empty WaitForPeers means the coordinator loop is skipped entirely.
            Assert.Empty(cfg.WaitForPeers);
        }

        /// <summary>
        /// Subsystems must receive headless mode in their <see cref="Models.SubsystemConfig"/>
        /// when the orchestrator is in headless mode.
        /// </summary>
        [Fact]
        public void AggregatedMode_HeadlessFlag_PropagatedToAllSubsystems()
        {
            var simHost = new MockSubsystem("SimHost");
            var ig      = new MockSubsystem("IG");
            var ios     = new MockSubsystem("ExCon");

            var orchestrator = new SubsystemOrchestrator(
                new ISubsystem[] { simHost, ig, ios },
                new RunnerOptions { Headless = true });

            orchestrator.Initialize();

            Assert.True(simHost.ReceivedConfig!.Headless, "SimHost must be headless");
            Assert.True(ig.ReceivedConfig!.Headless,      "IG must be headless");
            Assert.True(ios.ReceivedConfig!.Headless,     "ExCon must be headless");

            orchestrator.Shutdown();
        }

        /// <summary>
        /// <c>RunFrames(N)</c> must drive exactly N update ticks on every registered subsystem.
        /// </summary>
        [Fact]
        public void AggregatedMode_RunFrames_UpdatesAllSubsystems()
        {
            var simHost = new MockSubsystem("SimHost");
            var ig      = new MockSubsystem("IG");
            var ios     = new MockSubsystem("ExCon");

            var orchestrator = new SubsystemOrchestrator(
                new ISubsystem[] { simHost, ig, ios },
                new RunnerOptions { Headless = true });

            orchestrator.Initialize();
            orchestrator.RunFrames(10);

            Assert.Equal(10, simHost.UpdateCallCount);
            Assert.Equal(10, ig.UpdateCallCount);
            Assert.Equal(10, ios.UpdateCallCount);

            orchestrator.Shutdown();
        }
    }

    /// <summary>
    /// End-to-end integration tests for <see cref="HeadlessTestExecutor"/>.
    ///
    /// <para>Verifies that a real <see cref="TestScript"/> runs to completion with
    /// a <c>PASS</c> result and that a structured JSON report is saved to disk.</para>
    /// </summary>
    public class HeadlessExecutorIntegrationTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static RunnerConfiguration HeadlessAllNoWait()
        {
            var cfg = new RunnerConfiguration
            {
                ModeString = "all",
                Headless   = true,
                NoWait     = true,
            };
            cfg.Validate();
            return cfg;
        }

        /// <summary>Writes JSON to a temporary file and returns the path.</summary>
        private static string WriteTempScript(string json)
        {
            var path = Path.GetTempFileName() + ".json";
            File.WriteAllText(path, json);
            return path;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A minimal script — tick 10 frames and assert the return value —
        /// must complete with exit code 0 (PASS) and produce a report file.
        /// </summary>
        [Fact]
        public async Task HeadlessExecution_TickAndAssert_PassesWithExitCode0()
        {
            // Script: tick 10 frames at t=0, assert frames_run==10, finish after 0.3 s
            const string scriptJson = """
                {
                  "TestName": "Runner Tick Integration Test",
                  "Duration": 0.3,
                  "Steps": [
                    {
                      "Time": 0.0,
                      "Action": "tick",
                      "Args": { "frames": 10 },
                      "Assert": { "frames_run": { "Exactly": 10.0 } }
                    }
                  ]
                }
                """;

            var scriptPath = WriteTempScript(scriptJson);

            // Capture any report files created in the working directory
            var reportsBefore = Directory.GetFiles(".", "test_report_*.json");

            try
            {
                var orchestrator = new SubsystemOrchestrator(
                    new ISubsystem[] { new MockSubsystem("All") },
                    new RunnerOptions { Headless = true });

                ILogger logger = new NullTestLogger();
                var executor = new HeadlessTestExecutor(orchestrator, scriptPath, logger);

                int exitCode = await executor.RunAsync();

                // Main assertion: test PASSED
                Assert.Equal(0, exitCode);

                // A structured JSON report must have been written
                var reportsAfter = Directory.GetFiles(".", "test_report_*.json");
                Assert.True(reportsAfter.Length > reportsBefore.Length,
                    "HeadlessTestExecutor must create a test_report_{timestamp}.json file");
            }
            finally
            {
                // Clean up temp script and any generated reports
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
                foreach (var f in Directory.GetFiles(".", "test_report_*.json"))
                {
                    try { File.Delete(f); } catch { /* best-effort */ }
                }
            }
        }

        /// <summary>
        /// A script that spawns an entity and immediately reads its position must
        /// produce a PASS result, confirming that <c>spawn</c> + <c>assert_position</c>
        /// round-trip correctly through the ECS world.
        /// </summary>
        [Fact]
        public async Task HeadlessExecution_SpawnAndAssertPosition_PassesWithExitCode0()
        {
            // Script: spawn at (10, 20, 30), tick 5 frames, read back position at t=0.2s
            const string scriptJson = """
                {
                  "TestName": "Spawn And Assert Position",
                  "Duration": 0.4,
                  "Steps": [
                    {
                      "Time": 0.0,
                      "Action": "spawn",
                      "Args": { "x": 10.0, "y": 20.0, "z": 30.0 }
                    },
                    {
                      "Time": 0.05,
                      "Action": "tick",
                      "Args": { "frames": 5 }
                    },
                    {
                      "Time": 0.1,
                      "Action": "assert_position",
                      "Args": { "entity_id": 0 },
                      "Assert": {
                        "x": { "Exactly": 10.0 },
                        "z": { "Exactly": 30.0 }
                      }
                    }
                  ]
                }
                """;

            var scriptPath = WriteTempScript(scriptJson);

            try
            {
                var orchestrator = new SubsystemOrchestrator(
                    new ISubsystem[] { new MockSubsystem("All") },
                    new RunnerOptions { Headless = true });

                // Provide an EntityRepository so spawn / assert_position handlers work.
                using var world = new EntityRepository();
                world.RegisterComponent<SimTransform>();

                ILogger logger = new NullTestLogger();
                var executor = new HeadlessTestExecutor(orchestrator, scriptPath, logger);
                executor.RegisterHandler(new Hrot.ClusterRunner.Testing.SpawnActionHandler(world, logger));
                executor.RegisterHandler(new Hrot.ClusterRunner.Testing.AssertPositionActionHandler(world, logger));

                int exitCode = await executor.RunAsync();

                Assert.Equal(0, exitCode);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
                foreach (var f in Directory.GetFiles(".", "test_report_*.json"))
                {
                    try { File.Delete(f); } catch { /* best-effort */ }
                }
            }
        }

        /// <summary>
        /// A script with a deliberately failing assertion must produce exit code 1 (FAIL)
        /// and a report with <c>Status == "FAIL"</c> written to disk.
        /// </summary>
        [Fact]
        public async Task HeadlessExecution_FailingAssertion_ExitCode1AndFailStatus()
        {
            // Tick 5 frames but assert frames_run == 99 ← will always fail
            const string scriptJson = """
                {
                  "TestName": "Deliberate Failure Test",
                  "Duration": 0.3,
                  "Steps": [
                    {
                      "Time": 0.0,
                      "Action": "tick",
                      "Args": { "frames": 5 },
                      "Assert": { "frames_run": { "Exactly": 99.0 } }
                    }
                  ]
                }
                """;

            var scriptPath = WriteTempScript(scriptJson);

            try
            {
                var orchestrator = new SubsystemOrchestrator(
                    new ISubsystem[] { new MockSubsystem("All") },
                    new RunnerOptions { Headless = true });

                ILogger logger = new NullTestLogger();
                var executor = new HeadlessTestExecutor(orchestrator, scriptPath, logger);

                int exitCode = await executor.RunAsync();

                // Must return 1 because the assertion failed
                Assert.Equal(1, exitCode);

                // The report file must exist and reflect FAIL status
                var reportFiles = Directory.GetFiles(".", "test_report_*.json");
                Assert.True(reportFiles.Length > 0, "A report must be written even on failure");

                var reportJson = await File.ReadAllTextAsync(reportFiles[0]);
                Assert.Contains("\"FAIL\"", reportJson);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
                foreach (var f in Directory.GetFiles(".", "test_report_*.json"))
                {
                    try { File.Delete(f); } catch { /* best-effort */ }
                }
            }
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="ILogger"/> implementation that discards all log output.
        /// Avoids a hard dependency on <c>Microsoft.Extensions.Logging.Abstractions</c>
        /// in the test project.
        /// </summary>
        private sealed class NullTestLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}
