using System;
using System.Text.Json;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration.Handlers;
using Fdp.Core;
using Xunit;

namespace Hrot.SimHost.Tests
{
    // ── Minimal test component (ComponentId 210) ─────────────────────────────
    // Distinct from CkptPos (206) used by CheckpointClusterOpHandlerTests so both
    // test classes can register their own component without ID collisions.

    [ComponentId(210)]
    internal struct PreviewTestPos
    {
        public float X, Y, Z;
    }

    /// <summary>
    /// Tests for <see cref="PreviewClusterOpHandler"/> — CGF1-S0309 success conditions.
    /// </summary>
    public sealed class PreviewClusterOpHandlerTests : IDisposable
    {
        private readonly EntityRepository _liveRepo;

        public PreviewClusterOpHandlerTests()
        {
            _liveRepo = new EntityRepository();
            _liveRepo.RegisterComponent<PreviewTestPos>();
        }

        public void Dispose() => _liveRepo.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private static NodeOpCommand MakePrepareStateCmd(ClusterState target) => new()
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = (int)target }),
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

            var handler = new PreviewClusterOpHandler(_liveRepo);
            var cmd     = MakePrepareStateCmd(ClusterState.LoadingPreview);

            await handler.PrepareAsync(cmd, default);
            handler.Commit(cmd, _liveRepo);

            var snap = handler.TestHook_Snap;
            Assert.NotNull(snap);
            Assert.Equal(4, snap.EntityCount);  // §CGF1-S0309: EntityCount == 4 on snapshot

            // Spot-check first entity's component values.
            Assert.True(snap.HasComponent<PreviewTestPos>(entities[0]));
            var snapPos = snap.GetComponent<PreviewTestPos>(entities[0]);
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

            var handler = new PreviewClusterOpHandler(_liveRepo);

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
            var handler  = new PreviewClusterOpHandler(_liveRepo);
            var loadCmd  = MakePrepareStateCmd(ClusterState.LoadingPreview);
            var unloadCmd = MakePrepareStateCmd(ClusterState.UnloadingPreview);

            await handler.PrepareAsync(loadCmd, default);
            handler.Commit(loadCmd, _liveRepo);

            Assert.NotNull(handler.TestHook_Snap);

            await handler.PrepareAsync(unloadCmd, default);
            handler.Commit(unloadCmd, _liveRepo);

            Assert.Null(handler.TestHook_Snap);
        }

        // ── S0309-T4: Abort during dry run discards snapshot ─────────────────

        /// <summary>
        /// If the prepare/commit cycle is aborted during a dry-run session the
        /// snapshot must be discarded and the internal field nulled.
        /// </summary>
        [Fact]
        public async Task Abort_DuringLoadingPreview_DiscardsSnap()
        {
            var handler = new PreviewClusterOpHandler(_liveRepo);
            var loadCmd = MakePrepareStateCmd(ClusterState.LoadingPreview);

            await handler.PrepareAsync(loadCmd, default);
            handler.Commit(loadCmd, _liveRepo);

            Assert.NotNull(handler.TestHook_Snap);

            handler.Abort(loadCmd, _liveRepo);

            Assert.Null(handler.TestHook_Snap);
        }

        // ── S0309-T5: Non-dry-run PrepareState targets are no-ops ────────────

        /// <summary>
        /// A <see cref="NodeOpType.PrepareState"/> targeting any state other than
        /// LoadingPreview or UnloadingPreview must not mutate the snapshot or live repo.
        /// </summary>
        [Fact]
        public async Task OtherPrepareStateTargets_AreNoOps()
        {
            var handler = new PreviewClusterOpHandler(_liveRepo);

            foreach (var state in new[]
            {
                ClusterState.Idle, ClusterState.LoadingEdit, ClusterState.OperatingEdit,
                ClusterState.LoadingLive, ClusterState.OperatingLive, ClusterState.LoadingReplay,
            })
            {
                var cmd = MakePrepareStateCmd(state);
                await handler.PrepareAsync(cmd, default);
                handler.Commit(cmd, _liveRepo);

                Assert.Null(handler.TestHook_Snap);
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

            var handler   = new PreviewClusterOpHandler(_liveRepo);
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
    }
}
