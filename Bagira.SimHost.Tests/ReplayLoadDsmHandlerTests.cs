using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Bagira.SimHost.Modules.Orchestration;
using Bagira.SimHost.Modules.Orchestration.Handlers;
using Fdp.Kernel;
using FDP.Toolkit.Replay;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core;
using ModuleHost.Core.Scheduling;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Integration tests for <see cref="ReplayLoadDsmHandler"/> (CGF1-S0304).
    /// </summary>
    public class ReplayLoadDsmHandlerTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator _evtAcc;
        private readonly ModuleHostKernel _kernel;
        private readonly string           _tempDir;

        public ReplayLoadDsmHandlerTests()
        {
            _world  = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _evtAcc = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"ReplayLoadDsmHandlerTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── CGF1-S0304 success condition 6 ───────────────────────────────────────

        /// <summary>
        /// Full replay transition:
        /// <list type="number">
        ///   <item>Creates a small recording (5 ticks) so a real <c>.fdp</c> file exists.</item>
        ///   <item>Calls <c>PrepareAsync(PrepareReplay)</c> on the handler, triggering
        ///   <see cref="EcsRecordReplayController.PrepareReplayAsync"/>.</item>
        ///   <item>Calls <c>Commit(PrepareReplay)</c> on the handler.</item>
        ///   <item>Asserts <see cref="SimulationSystemGroup.Enabled"/> is <c>false</c>.</item>
        ///   <item>Asserts <see cref="GhostCreationSystem.BypassLifecycle"/> is <c>true</c>.</item>
        /// </list>
        /// </summary>
        [Fact(Timeout = 20_000)]
        public async Task FullReplayTransition_DisablesSimGroups()
        {
            // ── Step 1: create a recording so PrepareReplayAsync can open the file. ──
            var drillId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime      = 0.016f,
                TimeScale      = 1.0f,
                TotalWallTicks = 10_000L,
            });

            await controller.PrepareRecordingAsync(drillId, _tempDir);

            // Drive a few ticks so the file has at least one frame.
            for (int i = 0; i < 5; i++)
            {
                _world.SetSingletonUnmanaged(new GlobalTime
                {
                    DeltaTime      = 0.016f,
                    TimeScale      = 1.0f,
                    TotalWallTicks = 10_000L + i * 16L,
                });
                await Task.Delay(20);
            }

            await controller.FinalizeRecordingAsync();

            // ── Step 2: build the handler. ──
            var simGroup         = new SimulationSystemGroup();
            var entityMap        = new NetworkEntityMap();
            var ghostSys         = new GhostCreationSystem(entityMap);
            var lifecycleGroup   = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReplayLoadDsmHandler(
                controller,
                simGroup,
                lifecycleGroup,
                ghostSys,
                statusWriter:     null,
                nodeId:           1,
                storageDirectory: _tempDir);

            // ── Step 3: dispatch PrepareReplay → Commit. ──
            var payload = $"{{\"DrillId\":\"{drillId:D}\"}}";
            var cmd = new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.PrepareReplay,
                PayloadJson   = payload,
            };

            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, repo: null);

            // ── Step 4: stop kernel loop. ──
            cts.Cancel();
            await loopTask;

            // ── Step 5: assertions. ──
            Assert.False(simGroup.Enabled,
                "SimulationSystemGroup.Enabled must be false during RunningReplay.");
            Assert.False(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be false during RunningReplay.");
            Assert.True(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be true during RunningReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task FinalizeReplay_ReEnablesSimGroups()
        {
            // ── Step 1: create a recording. ──
            var drillId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 1_000L,
            });

            await controller.PrepareRecordingAsync(drillId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler and run PrepareReplay → Commit. ──
            var simGroup       = new SimulationSystemGroup();
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReplayLoadDsmHandler(
                controller, simGroup, lifecycleGroup, ghostSys,
                statusWriter: null, nodeId: 1, storageDirectory: _tempDir);

            var prepareCmd = new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.PrepareReplay,
                PayloadJson   = $"{{\"DrillId\":\"{drillId:D}\"}}",
            };
            await handler.PrepareAsync(prepareCmd, CancellationToken.None);
            handler.Commit(prepareCmd, repo: null);

            // Sim group is now disabled.
            Assert.False(simGroup.Enabled);

            // ── Step 3: dispatch FinalizeReplay → Commit. ──
            var finalizeCmd = new NodeOpCommand
            {
                TransactionId = Guid.NewGuid(),
                Operation     = NodeOpType.FinalizeReplay,
                PayloadJson   = string.Empty,
            };
            await handler.PrepareAsync(finalizeCmd, CancellationToken.None);
            handler.Commit(finalizeCmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: assertions. ──
            Assert.True(simGroup.Enabled,
                "SimulationSystemGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.False(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be reset to false after FinalizeReplay.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

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
