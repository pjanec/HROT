using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.SimHost.Modules.Orchestration;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Scheduling;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Integration tests for <see cref="ReferenceReplayLoadHandler"/> (CGF1-S0304).
    /// </summary>
    public class ReplayLoadClusterOpHandlerTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator _evtAcc;
        private readonly ModuleHostKernel _kernel;
        private readonly string           _tempDir;

        public ReplayLoadClusterOpHandlerTests()
        {
            _world  = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _evtAcc = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"ReplayLoadClusterOpHandlerTests_{Guid.NewGuid():N}");
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
        ///   <item>Asserts <see cref="TogglableSimulationGroup.Enabled"/> is <c>false</c>.</item>
        ///   <item>Asserts <see cref="GhostCreationSystem.BypassLifecycle"/> is <c>true</c>.</item>
        /// </list>
        /// </summary>
        [Fact(Timeout = 20_000)]
        public async Task FullReplayTransition_DisablesSimGroups()
        {
            // ── Step 1: create a recording so PrepareReplayAsync can open the file. ──
            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime      = 0.016f,
                TimeScale      = 1.0f,
                TotalWallTicks = 10_000L,
            });

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);

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
            var inputGroup       = new TogglableInputGroup("test-input");
            var simGroup         = new TogglableSimulationGroup("test");
            var postSimGroup     = new TogglablePostSimulationGroup("test-postsim");
            var entityMap        = new NetworkEntityMap();
            var ghostSys         = new GhostCreationSystem(entityMap);
            var lifecycleGroup   = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            // ── Step 3: dispatch PrepareReplay → Commit. ──
            var payload = exerciseId;  // Guid directly as DomainPayload
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = payload,
            };

            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, repo: null);

            // ── Step 4: stop kernel loop. ──
            cts.Cancel();
            await loopTask;

            // ── Step 5: assertions. ──
            Assert.False(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be false during RunningReplay.");
            Assert.False(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be false during RunningReplay.");
            Assert.True(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be true during RunningReplay.");
            Assert.False(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be false during RunningReplay.");
            Assert.False(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be false during RunningReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task FinalizeReplay_ReEnablesSimGroups()
        {
            // ── Step 1: create a recording. ──
            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 1_000L,
            });

            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler and run PrepareReplay → Commit. ──
            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            var prepareCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };
            await handler.PrepareAsync(prepareCmd, CancellationToken.None);
            handler.Commit(prepareCmd, repo: null);

            // Sim group is now disabled.
            Assert.False(simGroup.Enabled);
            Assert.False(inputGroup.Enabled);
            Assert.False(postSimGroup.Enabled);

            // ── Step 3: dispatch FinalizeReplay → Commit. ──
            var finalizeCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.FinalizeReplay,
                DomainPayload = null,
            };
            await handler.PrepareAsync(finalizeCmd, CancellationToken.None);
            handler.Commit(finalizeCmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: assertions. ──
            Assert.True(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.False(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be reset to false after FinalizeReplay.");
            Assert.True(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task PrepareReplay_DisablesAllFourGroups()
        {
            // ── Step 1: create a recording ──
            var exerciseId = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime      = 0.016f,
                TimeScale      = 1.0f,
                TotalWallTicks = 10_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler with all four groups ──
            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test-sim");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            // ── Step 3: PrepareReplay → Commit ──
            var cmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };
            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: all four groups must be disabled ──
            Assert.False(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be false during RunningReplay.");
            Assert.False(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be false during RunningReplay.");
            Assert.False(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be false during RunningReplay.");
            Assert.False(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be false during RunningReplay.");
            Assert.True(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be true during RunningReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task FinalizeReplay_ReEnablesAllFourGroups()
        {
            // ── Step 1: create a recording ──
            var exerciseId = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 1_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler with all four groups, run PrepareReplay ──
            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test-sim");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            var prepareCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };
            await handler.PrepareAsync(prepareCmd, CancellationToken.None);
            handler.Commit(prepareCmd, repo: null);

            // All four groups are disabled.
            Assert.False(inputGroup.Enabled);
            Assert.False(simGroup.Enabled);
            Assert.False(postSimGroup.Enabled);
            Assert.False(lifecycleGroup.Enabled);

            // ── Step 3: FinalizeReplay → Commit ──
            var finalizeCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.FinalizeReplay,
                DomainPayload = null,
            };
            await handler.PrepareAsync(finalizeCmd, CancellationToken.None);
            handler.Commit(finalizeCmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: all four groups must be re-enabled ──
            Assert.True(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.True(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be re-enabled after FinalizeReplay.");
            Assert.False(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be reset to false after FinalizeReplay.");
        }

        [Fact(Timeout = 20_000)]
        public async Task PrepareLive_ReEnablesAllFourGroups()
        {
            // ── Step 1: create a recording ──
            var exerciseId = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 5_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: build handler with all four groups, run PrepareReplay ──
            var inputGroup     = new TogglableInputGroup("test-input");
            var simGroup       = new TogglableSimulationGroup("test-sim");
            var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller,
                inputGroup:    inputGroup,
                simGroup:      simGroup,
                postSimGroup:  postSimGroup,
                lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                storageDirectory: _tempDir);

            var prepareCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
                DomainPayload = exerciseId,
            };
            await handler.PrepareAsync(prepareCmd, CancellationToken.None);
            handler.Commit(prepareCmd, repo: null);

            // All four groups are now disabled.
            Assert.False(inputGroup.Enabled);
            Assert.False(simGroup.Enabled);
            Assert.False(postSimGroup.Enabled);

            // ── Step 3: PrepareLive (Live-from-Replay branch) → Commit ──
            var branchCmd = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareLive,
                DomainPayload = Guid.NewGuid(),  // new branched exercise ID
            };
            await handler.PrepareAsync(branchCmd, CancellationToken.None);
            handler.Commit(branchCmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: all four groups must be re-enabled ──
            Assert.True(inputGroup.Enabled,
                "TogglableInputGroup.Enabled must be re-enabled after PrepareLive branch.");
            Assert.True(simGroup.Enabled,
                "TogglableSimulationGroup.Enabled must be re-enabled after PrepareLive branch.");
            Assert.True(postSimGroup.Enabled,
                "TogglablePostSimulationGroup.Enabled must be re-enabled after PrepareLive branch.");
            Assert.True(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be re-enabled after PrepareLive branch.");
            Assert.False(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be reset after PrepareLive branch.");
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
