using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.SimHost.Modules.Orchestration;
using Fdp.Core;
using Fdp.Toolkit.Replay;
using Fdp.ModuleHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EcsRecordReplayController"/> (MOD1-P8T1).
    /// </summary>
    [Xunit.Collection(RecordReplayKernelLoopCollection.Name)]
    public class EcsRecordReplayControllerTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator  _evtAcc;
        private readonly ModuleHostKernel  _kernel;
        private readonly string            _tempDir;

        public EcsRecordReplayControllerTests()
        {
            _world   = new EntityRepository();
            _evtAcc  = new EventAccumulator();
            _kernel  = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"EcsRecordReplayControllerTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── MOD1-P8T1 success condition 1 ────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task PrepareRecordingAsync_InstallsRecordingModule()
        {
            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);

            // After await: module must be installed in the kernel.
            Assert.NotNull(controller.ActiveRecordingModule);
            Assert.True(_kernel.IsModuleInstalled(controller.ActiveRecordingModule!),
                "RecordingModule must be in the kernel after PrepareRecordingAsync.");

            // Cleanup — finalize before tearing down the kernel loop.
            await controller.FinalizeRecordingAsync();

            cts.Cancel();
            await loopTask;
        }

        // ── MOD1-P8T1 success condition 2 ────────────────────────────────────────

        [Fact(Timeout = 10_000)]
        public async Task FinalizeRecordingAsync_UninstallsRecordingModule()
        {
            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            var installedModule = controller.ActiveRecordingModule;
            Assert.NotNull(installedModule);

            await controller.FinalizeRecordingAsync();

            // After finalize: module must be gone and property cleared.
            Assert.Null(controller.ActiveRecordingModule);
            Assert.False(_kernel.IsModuleInstalled(installedModule!),
                "RecordingModule must be uninstalled after FinalizeRecordingAsync.");

            cts.Cancel();
            await loopTask;
        }

        // ── MOD1-P8T1 success condition 3 ────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task StartStopEpisodeRecording_InstallsAndUninstallsEpisodeModule()
        {
            var episodeId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            // Create the episodes directory that the controller will write into.
            var episodesDir = Path.Combine(_tempDir, "episodes");
            Directory.CreateDirectory(episodesDir);

            await controller.StartEpisodeRecordingAsync(episodeId, _tempDir);

            // Episode module is live — ActiveRecordingModule is unaffected (no global recording).
            Assert.Null(controller.ActiveRecordingModule);

            await controller.StopEpisodeRecordingAsync(episodeId);

            cts.Cancel();
            await loopTask;
        }

        // ── MOD1-P8T1 success condition 4 ────────────────────────────────────────

        [Fact(Timeout = 15_000)]
        public async Task PrepareRecordingAsync_AndEpisodeRecording_RunConcurrently()
        {
            var exerciseId    = Guid.NewGuid();
            var episodeId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            // Create episodes sub-directory.
            Directory.CreateDirectory(Path.Combine(_tempDir, "episodes"));

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            await controller.StartEpisodeRecordingAsync(episodeId, _tempDir);

            // Both modules independently installed.
            Assert.NotNull(controller.ActiveRecordingModule);
            Assert.True(_kernel.IsModuleInstalled(controller.ActiveRecordingModule!));

            await controller.StopEpisodeRecordingAsync(episodeId);
            await controller.FinalizeRecordingAsync();

            cts.Cancel();
            await loopTask;
        }

        // ── CGF1-S0304 success condition 3 ───────────────────────────────────────

        /// <summary>
        /// After a 10-tick recording session is finalized:
        /// <list type="bullet">
        ///   <item>A <c>.meta.json</c> file exists at the expected path.</item>
        ///   <item>The JSON contains <c>"MaxNetworkId"</c> &gt;= 0 (well-formed manifest).</item>
        /// </list>
        /// When <c>maxNetworkId > 0</c> is passed to <c>FinalizeRecordingAsync</c> the
        /// value is faithfully round-tripped to disk (CGF1-S0304).
        /// </summary>
        [Fact(Timeout = 15_000)]
        public async Task FinalizeRecording_WritesMetaJson()
        {
            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new Fdp.Core.GlobalTime
            {
                DeltaTime       = 0.016f,
                TimeScale       = 1.0f,
                TotalWallTicks  = 1000L,
            });

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);

            // Drive 10 update frames so the recorder has real data.
            for (int i = 0; i < 10; i++)
            {
                _world.SetSingletonUnmanaged(new Fdp.Core.GlobalTime
                {
                    DeltaTime       = 0.016f,
                    TimeScale       = 1.0f,
                    TotalWallTicks  = 1000L + i * 16L,
                });
                await Task.Delay(20);   // let kernel loop tick
            }

            // Finalize with a known MaxNetworkId so the value is verifiable.
            const long expectedMaxNetworkId = 42L;
            await controller.FinalizeRecordingAsync(maxNetworkId: expectedMaxNetworkId);

            cts.Cancel();
            await loopTask;

            // Assert .meta.json exists somewhere under _tempDir (search recursively to be
            // agnostic of the exact exercise-directory naming convention).
            var metaFiles = Directory.GetFiles(_tempDir, "*.meta.json", SearchOption.AllDirectories);
            Assert.True(metaFiles.Length > 0,
                $"Expected at least one .meta.json under {_tempDir} after FinalizeRecordingAsync.");

            var metaPath = metaFiles[0];
            Assert.True(File.Exists(metaPath), $"Expected .meta.json at {metaPath}.");

            var json = await File.ReadAllTextAsync(metaPath);
            Assert.Contains("MaxNetworkId", json);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("MaxNetworkId", out var prop),
                "meta.json must contain MaxNetworkId field.");
            Assert.Equal(expectedMaxNetworkId, prop.GetInt64());
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>An ingress-shaped system: gated like the real one, counting the exit discards it is told about.</summary>
        [Fdp.ModuleHost.Abstractions.UpdateInPhase(Fdp.ModuleHost.Abstractions.SystemPhase.Input)]
        [Fdp.ModuleHost.Abstractions.TickMode(Fdp.ModuleHost.Abstractions.TickPolicy.EveryFrame)]
        private sealed class GatedSpy : Fdp.ModuleHost.Abstractions.IEcsModuleSystem, Fdp.Interfaces.IReplayRestoreGated
        {
            public Func<bool>? IsReplayRestoring { get; set; }
            public int ExitDiscards { get; private set; }
            public void DiscardAtReplayExit() => ExitDiscards++;
            public void Execute(Fdp.ModuleHost.Abstractions.ISimulationView view, float dt) { }
        }

        /// <summary>
        /// ⭐⭐⭐ CE-3329 W5 (DESIGN_Replay_Time_Axis_And_Review §12u) — from the replay's teardown until the exit's RELEASE the ingress stays
        /// gated (nothing is polled while the world is being put back); the release tells every gated system to discard what its readers
        /// hold, once, and lifts the gate. A node that starts a live recording lifts a hold an aborted exit left behind.
        /// 🔴 Without the hold the first frame after the teardown polls the replay-era readers into the world.
        /// </summary>
        [Fact(Timeout = 15_000)]
        public async Task CE3329_W5_TheIngressStaysGatedFromTheTeardownUntilTheRelease_WhichDiscardsOnce()
        {
            using var world = new EntityRepository();
            using var kernel = new ModuleHostKernel(world, new EventAccumulator());
            var spy = new GatedSpy();
            kernel.RegisterGlobalSystem(spy);
            kernel.InitializeForTest();
            var controller = new EcsRecordReplayController(kernel, nodeId: 1, world);

            using var cts = new CancellationTokenSource();
            var loopTask = RunKernelLoop(kernel, cts.Token);

            await controller.PrepareReplayAsync(Guid.NewGuid(), _tempDir);   // no recording here: a listener — it polls the wire
            Assert.False(spy.IsReplayRestoring!());

            await controller.TeardownReplayAsync();
            Assert.True(spy.IsReplayRestoring!());      // ⭐ held: the world is being put back
            Assert.Equal(0, spy.ExitDiscards);

            controller.ReleaseReplayExit();
            Assert.False(spy.IsReplayRestoring!());
            Assert.Equal(1, spy.ExitDiscards);
            controller.ReleaseReplayExit();             // nothing held: nothing to discard
            Assert.Equal(1, spy.ExitDiscards);

            // an exit that never released (an abort): the next live recording lifts it
            await controller.PrepareReplayAsync(Guid.NewGuid(), _tempDir);
            await controller.TeardownReplayAsync();
            Assert.True(spy.IsReplayRestoring!());
            await controller.PrepareRecordingAsync(Guid.NewGuid(), _tempDir);
            Assert.False(spy.IsReplayRestoring!());
            Assert.Equal(2, spy.ExitDiscards);
            await controller.FinalizeRecordingAsync();

            cts.Cancel();
            await loopTask;
        }

        private static Task RunKernelLoop(ModuleHostKernel kernel, CancellationToken ct) =>
            Task.Run(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { kernel.Update(0.016f); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.Error.WriteLine($"[KernelLoop] {ex.Message}");
                    }
                    Thread.Sleep(16);
                }
            }, CancellationToken.None);
    }
}
