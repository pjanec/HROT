using System;
using System.Numerics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Common.Orchestration;
using Hrot.SimHost.Modules.Orchestration;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core;
using ModuleHost.Core.Scheduling;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Integration tests for the Live-from-Replay branch (CGF1-S0305).
    ///
    /// <para>
    /// These tests exercise <see cref="EcsRecordReplayController.TeardownReplayAsync"/>
    /// and the <see cref="ReferenceReplayLoadHandler"/> <c>PrepareLive</c> (operationId=9)
    /// path, verifying that entity state is preserved in-place after teardown and that
    /// the recording module is properly installed for the branched exercise.
    /// </para>
    /// </summary>
    public sealed class LiveFromReplayTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator  _evtAcc;
        private readonly ModuleHostKernel  _kernel;
        private readonly string            _tempDir;

        public LiveFromReplayTests()
        {
            _world  = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _evtAcc = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"LiveFromReplayTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── CGF1-S0305 success condition: TeardownReplay_PreservesEntityRepositoryState ──

        /// <summary>
        /// Verifies that <see cref="EcsRecordReplayController.TeardownReplayAsync"/>
        /// leaves the <see cref="EntityRepository"/> at the historical (post-seek) state:
        /// entities from the recording are not wiped out when the replay module is uninstalled.
        ///
        /// <para>
        /// Flow: create 5 entities → record → finalize → open replay → seek → teardown.
        /// Assert <c>EntityCount == 5</c> after teardown.
        /// </para>
        /// </summary>
        [Fact(Timeout = 20_000)]
        public async Task TeardownReplay_PreservesEntityRepositoryState()
        {
            // ── Step 1: create 5 entities ──────────────────────────────────────
            for (int i = 0; i < 5; i++)
            {
                var e = _world.CreateEntity();
                _world.AddComponent(e, new SimTransform { Position = new Vector3(i, i * 2f, 0f) });
            }
            Assert.Equal(5, _world.EntityCount);

            // ── Step 2: record for several frames ─────────────────────────────
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

            for (int i = 0; i < 5; i++)
            {
                _world.SetSingletonUnmanaged(new GlobalTime
                {
                    DeltaTime      = 0.016f,
                    TimeScale      = 1.0f,
                    TotalWallTicks = 10_000L + i * 160_000L,
                });
                await Task.Delay(20);
            }

            await controller.FinalizeRecordingAsync();
            cts.Cancel();
            await loopTask;

            // ── Step 3: open replay and seek ──────────────────────────────────
            var cts2      = new CancellationTokenSource();
            var loopTask2 = RunKernelLoop(_kernel, cts2.Token);

            await controller.PrepareReplayAsync(exerciseId, _tempDir);

            // Seek to frame 2 (mid-point) — blasts historical entity state into _world.
            await controller.ActiveReplayModule!.SeekToFrameAsync(2);

            // ── Step 4: teardown replay ───────────────────────────────────────
            await controller.TeardownReplayAsync();
            cts2.Cancel();
            await loopTask2;

            // ── Step 5: entity state must be preserved after teardown ─────────
            Assert.Equal(5, _world.EntityCount);
        }

        // ── CGF1-S0305 success condition: AfterBranch_RecordingModuleIsInstalled ──

        /// <summary>
        /// After <see cref="ReferenceReplayLoadHandler.PrepareAsync"/> handles a
        /// <c>PrepareLive</c> (operationId=9) command (the Live-from-Replay branch),
        /// the <see cref="EcsRecordReplayController"/> must have an active
        /// <see cref="FDP.Toolkit.Replay.RecordingModule"/> installed in the kernel scheduler.
        /// </summary>
        [Fact(Timeout = 20_000)]
        public async Task AfterBranch_RecordingModuleIsInstalled()
        {
            // ── Step 1: create entities and record ────────────────────────────
            for (int i = 0; i < 5; i++)
            {
                var e = _world.CreateEntity();
                _world.AddComponent(e, new SimTransform { Position = new Vector3(i, 0f, 0f) });
            }

            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 20_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: put system into replay state ──────────────────────────
            await controller.PrepareReplayAsync(exerciseId, _tempDir);

            // ── Step 3: issue PrepareLive (Live-from-Replay branch) ───────────
            var simGroup       = new SimulationSystemGroup();
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler      = new ReferenceReplayLoadHandler(
                controller, simGroup, lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                transport: null, nodeId: 1, storageDirectory: _tempDir);

            var branchedExerciseId = Guid.NewGuid();
            var branchCmd       = new OrchestrationCommand(
                Guid.NewGuid(), 0,
                ReferenceReplayLoadHandler.PrepareLiveOperationId,
                $"{{\"ExerciseId\":\"{branchedExerciseId:D}\"}}");

            await handler.PrepareAsync(branchCmd, CancellationToken.None);

            // ── Step 4: stop kernel loop ──────────────────────────────────────
            cts.Cancel();
            await loopTask;

            // ── Step 5: recording module must be installed ────────────────────
            Assert.NotNull(controller.ActiveRecordingModule);
            Assert.True(_kernel.IsModuleInstalled(controller.ActiveRecordingModule!),
                "RecordingModule must be installed in the kernel after the Live-from-Replay branch.");
        }

        // ── CGF1-S0305 success condition: AfterBranch_SimGroupsReEnabled ──────

        /// <summary>
        /// After <see cref="ReferenceReplayLoadHandler.Commit"/> handles a
        /// <c>PrepareLive</c> (operationId=9) command (the Live-from-Replay branch),
        /// <see cref="SimulationSystemGroup.Enabled"/> must be <c>true</c> and
        /// <see cref="GhostCreationSystem.BypassLifecycle"/> must be <c>false</c>.
        /// </summary>
        [Fact(Timeout = 20_000)]
        public async Task AfterBranch_SimGroupsReEnabled()
        {
            // ── Step 1: create entities and record ────────────────────────────
            for (int i = 0; i < 5; i++)
            {
                var e = _world.CreateEntity();
                _world.AddComponent(e, new SimTransform { Position = new Vector3(i, 0f, 0f) });
            }

            var exerciseId    = Guid.NewGuid();
            var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts      = new CancellationTokenSource();
            var       loopTask = RunKernelLoop(_kernel, cts.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 30_000L,
            });
            await controller.PrepareRecordingAsync(exerciseId, _tempDir);
            for (int i = 0; i < 5; i++) { await Task.Delay(20); }
            await controller.FinalizeRecordingAsync();

            // ── Step 2: put system into replay state (sets groups disabled) ───
            await controller.PrepareReplayAsync(exerciseId, _tempDir);

            var simGroup       = new SimulationSystemGroup();
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var handler = new ReferenceReplayLoadHandler(
                controller, simGroup, lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                transport: null, nodeId: 1, storageDirectory: _tempDir);

            // Simulate PrepareReplay commit so groups start disabled.
            var prepCmd = new OrchestrationCommand(
                Guid.NewGuid(), 0,
                ReferenceReplayLoadHandler.PrepareReplayOperationId,
                $"{{\"ExerciseId\":\"{exerciseId:D}\"}}");
            handler.Commit(prepCmd, repo: null);
            Assert.False(simGroup.Enabled, "SimulationSystemGroup must be disabled during replay.");

            // ── Step 3: issue PrepareLive (Live-from-Replay branch) ───────────
            var branchedExerciseId = Guid.NewGuid();
            var branchCmd       = new OrchestrationCommand(
                Guid.NewGuid(), 0,
                ReferenceReplayLoadHandler.PrepareLiveOperationId,
                $"{{\"ExerciseId\":\"{branchedExerciseId:D}\"}}");
            await handler.PrepareAsync(branchCmd, CancellationToken.None);
            handler.Commit(branchCmd, repo: null);

            cts.Cancel();
            await loopTask;

            // ── Step 4: assertions ────────────────────────────────────────────
            Assert.True(simGroup.Enabled,
                "SimulationSystemGroup.Enabled must be true after Live-from-Replay branch Commit.");
            Assert.True(lifecycleGroup.Enabled,
                "NetworkLifecycleSystemGroup.Enabled must be true after Live-from-Replay branch Commit.");
            Assert.False(ghostSys.BypassLifecycle,
                "GhostCreationSystem.BypassLifecycle must be false after Live-from-Replay branch Commit.");
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
