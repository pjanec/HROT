using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Bagira.Common.Orchestration;
using Bagira.SimHost.Modules.Orchestration;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core;
using ModuleHost.Core.Scheduling;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Integration tests for the full Live-from-Replay branch pipeline (§CGF1-S0305).
    ///
    /// <para>
    /// Tests in this class exercise the complete end-to-end branch flow:
    /// </para>
    /// <list type="number">
    ///   <item>Record a live drill (original recording).</item>
    ///   <item>Open the recording in replay and seek to a mid-point frame.</item>
    ///   <item>Execute the Live-from-Replay branch via
    ///     <see cref="ReferenceReplayLoadHandler.PrepareAsync"/> /
    ///     <see cref="ReferenceReplayLoadHandler.Commit"/>.</item>
    ///   <item>Record a branched drill that starts from the historical snapshot.</item>
    ///   <item>Assert that the branched <c>.fdp</c> file's first frame is a keyframe
    ///     containing the entity state captured at the seek point of the original recording.</item>
    /// </list>
    /// </summary>
    public sealed class FullBranchPipelineTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EventAccumulator _evtAcc;
        private readonly ModuleHostKernel _kernel;
        private readonly string           _tempDir;

        public FullBranchPipelineTests()
        {
            _world  = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _evtAcc = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, _evtAcc);
            _kernel.InitializeForTest();

            _tempDir = Path.Combine(
                Path.GetTempPath(),
                $"FullBranchPipelineTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            _kernel.Dispose();
            _world.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ── §CGF1-S0305 integration test ─────────────────────────────────────────

        /// <summary>
        /// Full branch pipeline integration test (§CGF1-S0305 success condition):
        /// </summary>
        /// <remarks>
        /// <list type="number">
        ///   <item>Runs 80+ ticks of live simulation (enough to exceed frame 50 in the recording).</item>
        ///   <item>Seeks the replay to frame 50 — blasts the saved ECS snapshot into <c>_world</c>.</item>
        ///   <item>Executes the Live-from-Replay branch via
        ///     <see cref="ReferenceReplayLoadHandler.PrepareAsync"/> + <see cref="ReferenceReplayLoadHandler.Commit"/>
        ///     (matching the production dispatch path fixed in BATCH-18 A.1).</item>
        ///   <item>Records 50 additional ticks of branched live simulation.</item>
        ///   <item>Asserts that the branched <c>.fdp</c> file contains a keyframe at
        ///     frame 0 whose entity count and component values match the
        ///     <c>_world</c> snapshot taken immediately after seeking to frame 50 of the
        ///     original recording.</item>
        /// </list>
        /// </remarks>
        [Fact(Timeout = 60_000)]
        public async Task BranchedRecording_CapturesHistoricalStateAsKeyframe()
        {
            // ══ Phase 1: create entities and record 80+ frames ═══════════════════════
            const int EntityCount = 5;
            for (int i = 0; i < EntityCount; i++)
            {
                var e = _world.CreateEntity();
                _world.AddComponent(e, new SimTransform
                {
                    Position = new Vector3(i * 3f, i * 5f, 0f),
                });
            }

            var originalDrillId = Guid.NewGuid();
            var controller      = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

            using var cts1     = new CancellationTokenSource();
            var       kernelL1 = RunKernelLoop(_kernel, cts1.Token);

            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime      = 0.016f,
                TimeScale      = 1.0f,
                TotalWallTicks = 10_000L,
            });
            await controller.PrepareRecordingAsync(originalDrillId, _tempDir);

            // Drive ~100 kernel ticks (16 ms sleep × ~100 iterations ≈ 1.6 s) so the
            // recording accumulates well beyond frame 50.
            for (int i = 0; i < 100; i++)
            {
                _world.SetSingletonUnmanaged(new GlobalTime
                {
                    DeltaTime      = 0.016f,
                    TimeScale      = 1.0f,
                    TotalWallTicks = 10_000L + i * 16_000L,
                });
                await Task.Delay(20);
            }

            await controller.FinalizeRecordingAsync();
            cts1.Cancel();
            await kernelL1;

            // ══ Phase 2: open replay and seek to frame 50 ════════════════════════════
            using var cts2     = new CancellationTokenSource();
            var       kernelL2 = RunKernelLoop(_kernel, cts2.Token);

            await controller.PrepareReplayAsync(originalDrillId, _tempDir);

            // Seek blasts historical entity state (frame 50) into _world.
            await controller.ActiveReplayModule!.SeekToFrameAsync(50);

            // ── Snapshot: capture entity count and SimTransform positions at frame 50 ──
            int frame50EntityCount = _world.EntityCount;
            var frame50Positions   = ReadSimTransformPositions(_world);

            // ══ Phase 3: execute the Live-from-Replay branch ═════════════════════════
            var simGroup       = new SimulationSystemGroup();
            var entityMap      = new NetworkEntityMap();
            var ghostSys       = new GhostCreationSystem(entityMap);
            var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

            var branchedDrillId = Guid.NewGuid();
            var branchCmd       = new OrchestrationCommand(
                Guid.NewGuid(), 0,
                ReferenceReplayLoadHandler.PrepareLiveOperationId,
                $"{{\"DrillId\":\"{branchedDrillId:D}\"}}");

            var handler = new ReferenceReplayLoadHandler(
                controller, simGroup, lifecycleGroup,
                bypass => ghostSys.BypassLifecycle = bypass,
                transport: null, nodeId: 1, storageDirectory: _tempDir);

            // Mirrors the fixed DrillSlave dispatch (BATCH-18 A.1/A.3): await PrepareAsync first.
            await handler.PrepareAsync(branchCmd, CancellationToken.None);
            handler.Commit(branchCmd, repo: null);

            // ══ Phase 4: record 50 frames of branched live simulation ═════════════════
            for (int i = 0; i < 50; i++)
            {
                _world.SetSingletonUnmanaged(new GlobalTime
                {
                    DeltaTime      = 0.016f,
                    TimeScale      = 1.0f,
                    TotalWallTicks = 20_000L + i * 16_000L,
                });
                await Task.Delay(20);
            }

            await controller.FinalizeRecordingAsync();
            cts2.Cancel();
            await kernelL2;

            // ══ Phase 5: read branched recording frame 0 — must be a keyframe ══════════
            var branchedFilePath = Path.Combine(_tempDir, branchedDrillId.ToString("D"), "node_1.fdp");
            Assert.True(File.Exists(branchedFilePath),
                $"Branched recording file not found: {branchedFilePath}");

            using var branchedRepo = new EntityRepository();
            branchedRepo.RegisterComponent<SimTransform>();

            using var reader = new RecordingReader(branchedFilePath);
            bool hasFrame = reader.ReadNextFrame(branchedRepo);
            Assert.True(hasFrame, "Branched recording file must contain at least one frame.");

            // ── Assert entity count matches frame-50 snapshot ─────────────────────────
            Assert.Equal(frame50EntityCount, branchedRepo.EntityCount);

            // ── Assert SimTransform positions match frame-50 snapshot ──────────────────
            // ReadSimTransformPositions is a sync helper to avoid ref-readonly-in-async (C# < 13).
            var branchedPositions = ReadSimTransformPositions(branchedRepo);
            int assertedCount = 0;
            foreach (var kvp in frame50Positions)
            {
                if (!branchedPositions.TryGetValue(kvp.Key, out var branchedPos))
                    continue; // entity may have a different index — count only

                Assert.Equal(kvp.Value.X, branchedPos.X, precision: 4);
                Assert.Equal(kvp.Value.Y, branchedPos.Y, precision: 4);
                Assert.Equal(kvp.Value.Z, branchedPos.Z, precision: 4);
                assertedCount++;
            }

            // At least one entity's position must have been matched to confirm the
            // branched keyframe captured real historical state, not a blank slate.
            Assert.True(assertedCount > 0 || frame50EntityCount == 0,
                "At least one entity position from frame 50 must be present in the branched keyframe.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────
        /// <summary>
        /// Reads all <see cref="SimTransform"/> positions from <paramref name="repo"/>
        /// into a plain dictionary, avoiding <c>ref readonly</c> in async callers.
        /// </summary>
        private static Dictionary<int, Vector3> ReadSimTransformPositions(EntityRepository repo)
        {
            var result = new Dictionary<int, Vector3>();
            var q = repo.Query().With<SimTransform>().Build();
            foreach (var e in q)
            {
                ref readonly var t = ref repo.GetComponentRO<SimTransform>(e);
                result[e.Index] = t.Position;
            }
            return result;
        }
        private static Task RunKernelLoop(ModuleHostKernel kernel, CancellationToken ct) =>
            Task.Run(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { kernel.Update(0.016f); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.Error.WriteLine($"[FullBranchKernelLoop] {ex.Message}");
                    }
                    Thread.Sleep(16);
                }
            }, CancellationToken.None);
    }
}
