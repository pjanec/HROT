using System;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    // ── Minimal test component (ComponentId 507) ─────────────────────────────
    // ⚠ Was 210 — PRODUCTION's IEqsTemplateRegistry id. The component-type registry is process-global,
    // so once any test in this process installed an EQS registry (EqsModuleTests, 2026-09-30) the first
    // of the two to register threw "Component ID collision" and failed unrelated classes — the exact
    // EpisodeTestPos/215 precedent (EpisodeInjectionTests). A TEST component lives at the top of the
    // id space (MAX_COMPONENT_TYPES = 512), away from anything production grows into.

    [ComponentId(507)]
    internal struct PreviewTestPos
    {
        public float X, Y, Z;
    }

    /// <summary>
    /// Tests for <see cref="ReferencePreviewHandler"/> — THE dry-run handler (CGF1-S0309 success conditions).
    /// <para>⭐ <c>D14</c> — re-homed from the editor's twin (<c>PreviewClusterOpHandler</c>, deleted): the same claims, now
    /// through the cluster slave's intent path AND the editor's local entry points, which reach the one handler.
    /// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1b U1.</para>
    /// </summary>
    public sealed class ReferencePreviewHandlerTests : IDisposable
    {
        private readonly EntityRepository _liveRepo;

        public ReferencePreviewHandlerTests()
        {
            _liveRepo = new EntityRepository();
            _liveRepo.RegisterComponent<PreviewTestPos>();
        }

        public void Dispose() => _liveRepo.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ExecuteNodeOpIntent MakePrepareStateCmd(ClusterState target) => new()
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareState,
            DomainPayload = new EditLoadHandlerPayload(ScenarioId: null, TargetState: target),
        };

        // ── S0309-T1: LoadingPreview captures live state ───────────────────────

        /// <summary>
        /// After a LoadingPreview commit the handler must hold a snapshot whose
        /// content matches the live repository at capture time.
        /// <para>
        /// Uses four entities to match the §CGF1-S0309 TASK-DETAIL success text
        /// ("four entities … <c>EntityCount == 4</c> on snapshot").
        /// </para>
        /// </summary>
        [Fact]
        public async Task LoadingPreview_SnapshotCapturesLiveState()
        {
            // Build a 4-entity live repo with known, distinct positions.
            var entities = new Entity[4];
            for (int i = 0; i < 4; i++)
            {
                entities[i] = _liveRepo.CreateEntity();
                _liveRepo.SetComponent(entities[i], new PreviewTestPos { X = i + 1f, Y = i + 2f, Z = i + 3f });
            }
            Assert.Equal(4, _liveRepo.EntityCount);

            var handler = new ReferencePreviewHandler(_liveRepo);
            var cmd     = MakePrepareStateCmd(ClusterState.LoadingPreview);

            await handler.PrepareAsync(cmd, default);
            handler.Commit(cmd, _liveRepo);

            // ⭐ CE-3299 B2 — the capture is a RAM keyframe (the flight recorder's format), not a repository: what it holds is proven by
            //   putting it back. Mutate every entity, rewind, and the four captured values return.
            Assert.NotNull(handler.TestHook_Keyframe);
            Assert.True(handler.TestHook_Keyframe!.SizeBytes > 0);
            for (int i = 0; i < 4; i++) _liveRepo.SetComponent(entities[i], new PreviewTestPos { X = -1f, Y = -1f, Z = -1f });

            var unload = MakePrepareStateCmd(ClusterState.UnloadingPreview);
            await handler.PrepareAsync(unload, default);
            handler.Commit(unload, _liveRepo);

            Assert.Equal(4, _liveRepo.EntityCount);  // §CGF1-S0309: EntityCount == 4 at the capture
            Assert.True(_liveRepo.HasComponent<PreviewTestPos>(entities[0]));
            var snapPos = _liveRepo.GetComponent<PreviewTestPos>(entities[0]);
            Assert.Equal(1f, snapPos.X);
            Assert.Equal(2f, snapPos.Y);
            Assert.Equal(3f, snapPos.Z);
        }

        // ── S0309-T2: UnloadingPreview restores pre-dry-run state ─────────────

        /// <summary>
        /// After an UnloadingPreview commit the live repository must reflect the state
        /// that was snapshotted at LoadingPreview time — reversing both component mutations
        /// AND entity creations that occurred during the dry-run session.
        ///
        /// <para>Specifically: a 5th entity spawned during the dry run must be absent
        /// after rewind (EntityCount == 4), proving that SyncFrom removes entities
        /// created during the dry run, not only rewinding component values.</para>
        /// </summary>
        [Fact]
        public async Task UnloadingPreview_RewindsLiveRepo()
        {
            // 1. Build a 4-entity live repo with known positions.
            var entities = new Entity[4];
            for (int i = 0; i < 4; i++)
            {
                entities[i] = _liveRepo.CreateEntity();
                _liveRepo.SetComponent(entities[i], new PreviewTestPos { X = i * 10f, Y = i * 20f, Z = i * 30f });
            }
            Assert.Equal(4, _liveRepo.EntityCount);

            var handler = new ReferencePreviewHandler(_liveRepo);

            // 2. Enter dry run — snapshot captures 4 entities with original positions.
            var loadCmd = MakePrepareStateCmd(ClusterState.LoadingPreview);
            await handler.PrepareAsync(loadCmd, default);
            handler.Commit(loadCmd, _liveRepo);

            // 3. Advance one frame so SyncDirtyChunks detects mutations after the snapshot.
            _liveRepo.Tick();

            // 4. Mutate an existing entity's component during the dry-run session.
            _liveRepo.SetComponent(entities[0], new PreviewTestPos { X = 99f, Y = 99f, Z = 99f });

            // 5. Spawn an extra (5th) entity during the dry-run session.
            _liveRepo.CreateEntity();
            Assert.Equal(5, _liveRepo.EntityCount);

            // 6. Exit dry run — live repo must rewind to pre-dry-run state.
            var unloadCmd = MakePrepareStateCmd(ClusterState.UnloadingPreview);
            await handler.PrepareAsync(unloadCmd, default);
            handler.Commit(unloadCmd, _liveRepo);

            // 7. Entity count must be back to 4 (5th entity removed by SyncFrom).
            Assert.Equal(4, _liveRepo.EntityCount);

            // 8. Mutated component must be reverted to its original value.
            var pos = _liveRepo.GetComponent<PreviewTestPos>(entities[0]);
            Assert.Equal(0f, pos.X);
            Assert.Equal(0f, pos.Y);
            Assert.Equal(0f, pos.Z);
        }

        // ── S0309-T3: UnloadingPreview disposes snapshot ───────────────────────

        /// <summary>
        /// After <see cref="ClusterState.UnloadingPreview"/> the internal snapshot must be
        /// nulled out so repeated calls do not attempt a second rewind.
        /// </summary>
        [Fact]
        public async Task UnloadingPreview_DisposesSnapshot()
        {
            var handler  = new ReferencePreviewHandler(_liveRepo);
            var loadCmd  = MakePrepareStateCmd(ClusterState.LoadingPreview);
            var unloadCmd = MakePrepareStateCmd(ClusterState.UnloadingPreview);

            await handler.PrepareAsync(loadCmd, default);
            handler.Commit(loadCmd, _liveRepo);

            Assert.NotNull(handler.TestHook_Keyframe);

            await handler.PrepareAsync(unloadCmd, default);
            handler.Commit(unloadCmd, _liveRepo);

            Assert.Null(handler.TestHook_Keyframe);
        }

        // ── S0309-T4: Abort during dry run discards snapshot ─────────────────

        /// <summary>
        /// If the prepare/commit cycle is aborted during a dry-run session the
        /// snapshot must be discarded and the internal field nulled.
        /// </summary>
        [Fact]
        public async Task Abort_DuringLoadingPreview_DiscardsSnap()
        {
            var handler = new ReferencePreviewHandler(_liveRepo);
            var loadCmd = MakePrepareStateCmd(ClusterState.LoadingPreview);

            await handler.PrepareAsync(loadCmd, default);
            handler.Commit(loadCmd, _liveRepo);

            Assert.NotNull(handler.TestHook_Keyframe);

            handler.Abort(loadCmd, _liveRepo);

            Assert.Null(handler.TestHook_Keyframe);
        }

        // ── S0309-T5: Non-dry-run PrepareState targets are no-ops ────────────

        /// <summary>
        /// A <see cref="NodeOpType.PrepareState"/> targeting any state other than
        /// LoadingPreview or UnloadingPreview must not mutate the snapshot or live repo.
        /// </summary>
        [Fact]
        public async Task OtherPrepareStateTargets_AreNoOps()
        {
            var handler = new ReferencePreviewHandler(_liveRepo);

            foreach (var state in new[]
            {
                ClusterState.Idle, ClusterState.LoadingEdit, ClusterState.OperatingEdit,
                ClusterState.LoadingLive, ClusterState.OperatingLive, ClusterState.LoadingReplay,
            })
            {
                var cmd = MakePrepareStateCmd(state);
                Assert.False(handler.CanHandle(cmd));   // the slave never hands it to this handler
                await handler.PrepareAsync(cmd, default);
                handler.Commit(cmd, _liveRepo);

                Assert.Null(handler.TestHook_Keyframe);
            }
        }

        // ── S0309-T6: UnloadingPreview with no snapshot logs warning silently ──

        /// <summary>
        /// Calling UnloadingPreview when no snapshot is held (e.g. a missed LoadingPreview)
        /// must not throw; it should log a warning and return without mutating state.
        /// </summary>
        [Fact]
        public async Task UnloadingPreview_WithNullSnap_LogsWarningAndReturns()
        {
            var e = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(e, new PreviewTestPos { X = 5f, Y = 5f, Z = 5f });

            var handler   = new ReferencePreviewHandler(_liveRepo);
            var unloadCmd = MakePrepareStateCmd(ClusterState.UnloadingPreview);

            // No LoadingPreview was committed — snapshot is null.
            var ex = await Record.ExceptionAsync(async () =>
            {
                await handler.PrepareAsync(unloadCmd, default);
                handler.Commit(unloadCmd, _liveRepo);
            });

            Assert.Null(ex);

            // Live repo must be unchanged.
            var pos = _liveRepo.GetComponent<PreviewTestPos>(e);
            Assert.Equal(5f, pos.X);
        }

        // ── D14: the local entry points reach the same handler ─────────────────

        /// <summary>
        /// ⭐ <c>D14</c> — the editor's toolbar drives <see cref="ReferencePreviewHandler.TriggerLoadingPreview"/> /
        /// <see cref="ReferencePreviewHandler.TriggerUnloadingPreview"/> on the SAME instance its slave holds; they capture
        /// and rewind exactly as the master's PrepareState does.
        /// </summary>
        [Fact]
        public void LocalEntryPoints_CaptureAndRewind()
        {
            var e = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(e, new PreviewTestPos { X = 1f });
            var handler = new ReferencePreviewHandler(_liveRepo);

            handler.TriggerLoadingPreview();
            Assert.True(handler.HasSnapshot);
            _liveRepo.Tick();
            _liveRepo.SetComponent(e, new PreviewTestPos { X = 42f });
            _liveRepo.CreateEntity();

            handler.TriggerUnloadingPreview();
            Assert.False(handler.HasSnapshot);
            Assert.Equal(1, _liveRepo.EntityCount);
            Assert.Equal(1f, _liveRepo.GetComponent<PreviewTestPos>(e).X);
        }

        /// <summary>
        /// ⭐ <c>D14</c> — two entry points reach one handler (the master's LoadingPreview, the editor's Play). A second
        /// capture while one is held must NOT move the rewind point: the Stop puts back the FIRST capture.
        /// </summary>
        [Fact]
        public async Task SecondCapture_WhileHeld_KeepsTheFirst()
        {
            var e = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(e, new PreviewTestPos { X = 1f });
            var handler = new ReferencePreviewHandler(_liveRepo);

            handler.TriggerLoadingPreview();               // the toolbar's Play
            _liveRepo.Tick();
            _liveRepo.SetComponent(e, new PreviewTestPos { X = 7f });

            var load = MakePrepareStateCmd(ClusterState.LoadingPreview);   // then the master's Preview
            await handler.PrepareAsync(load, default);
            handler.Commit(load, _liveRepo);

            handler.TriggerUnloadingPreview();
            Assert.Equal(1f, _liveRepo.GetComponent<PreviewTestPos>(e).X);
        }
    }
}
