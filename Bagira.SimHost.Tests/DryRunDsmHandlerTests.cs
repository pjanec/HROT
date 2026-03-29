using System;
using System.Text.Json;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration.Handlers;
using Fdp.Kernel;
using Xunit;

namespace Bagira.SimHost.Tests
{
    // ── Minimal test component (ComponentId 210) ─────────────────────────────
    // Distinct from CkptPos (206) used by CheckpointDsmHandlerTests so both
    // test classes can register their own component without ID collisions.

    [ComponentId(210)]
    internal struct DryRunTestPos
    {
        public float X, Y, Z;
    }

    /// <summary>
    /// Tests for <see cref="DryRunDsmHandler"/> — CGF1-S0309 success conditions.
    /// </summary>
    public sealed class DryRunDsmHandlerTests : IDisposable
    {
        private readonly EntityRepository _liveRepo;

        public DryRunDsmHandlerTests()
        {
            _liveRepo = new EntityRepository();
            _liveRepo.RegisterComponent<DryRunTestPos>();
        }

        public void Dispose() => _liveRepo.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private static NodeOpCommand MakePrepareStateCmd(DSMState target) => new()
        {
            TransactionId = Guid.NewGuid(),
            Operation     = NodeOpType.PrepareState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = (int)target }),
        };

        // ── S0309-T1: LoadingDryRun captures live state ───────────────────────

        /// <summary>
        /// After a LoadingDryRun commit the handler must hold a snapshot whose
        /// content matches the live repository at capture time.
        /// </summary>
        [Fact]
        public async Task LoadingDryRun_SnapshotCapturesLiveState()
        {
            var e = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(e, new DryRunTestPos { X = 1f, Y = 2f, Z = 3f });

            var handler = new DryRunDsmHandler(_liveRepo);
            var cmd     = MakePrepareStateCmd(DSMState.LoadingDryRun);

            await handler.PrepareAsync(cmd, default);
            handler.Commit(cmd, _liveRepo);

            var snap = handler.TestHook_Snap;
            Assert.NotNull(snap);
            Assert.True(snap.HasComponent<DryRunTestPos>(e));

            var snapPos = snap.GetComponent<DryRunTestPos>(e);
            Assert.Equal(1f, snapPos.X);
            Assert.Equal(2f, snapPos.Y);
            Assert.Equal(3f, snapPos.Z);
        }

        // ── S0309-T2: UnloadingDryRun restores pre-dry-run state ─────────────

        /// <summary>
        /// After an UnloadingDryRun commit the live repository must reflect the state
        /// that was snapshotted at LoadingDryRun time, reversing any in-flight changes.
        /// </summary>
        [Fact]
        public async Task UnloadingDryRun_RewindsLiveRepo()
        {
            var e = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(e, new DryRunTestPos { X = 10f, Y = 20f, Z = 30f });

            var handler = new DryRunDsmHandler(_liveRepo);

            // 1. Enter dry run — snapshot captures (10, 20, 30).
            var loadCmd = MakePrepareStateCmd(DSMState.LoadingDryRun);
            await handler.PrepareAsync(loadCmd, default);
            handler.Commit(loadCmd, _liveRepo);

            // 2. Advance one frame (simulates normal engine tick during the dry-run session).
            //    Without Tick(), SetComponent reuses the same global version and chunk version
            //    matches the snapshot, so SyncDirtyChunks would skip the rewind copy.
            _liveRepo.Tick();

            // 3. Simulate in-flight dry-run mutation.
            _liveRepo.SetComponent(e, new DryRunTestPos { X = 99f, Y = 99f, Z = 99f });

            // 3. Exit dry run — live repo must rewind to (10, 20, 30).
            var unloadCmd = MakePrepareStateCmd(DSMState.UnloadingDryRun);
            await handler.PrepareAsync(unloadCmd, default);
            handler.Commit(unloadCmd, _liveRepo);

            var pos = _liveRepo.GetComponent<DryRunTestPos>(e);
            Assert.Equal(10f, pos.X);
            Assert.Equal(20f, pos.Y);
            Assert.Equal(30f, pos.Z);
        }

        // ── S0309-T3: UnloadingDryRun disposes snapshot ───────────────────────

        /// <summary>
        /// After <see cref="DSMState.UnloadingDryRun"/> the internal snapshot must be
        /// nulled out so repeated calls do not attempt a second rewind.
        /// </summary>
        [Fact]
        public async Task UnloadingDryRun_DisposesSnapshot()
        {
            var handler  = new DryRunDsmHandler(_liveRepo);
            var loadCmd  = MakePrepareStateCmd(DSMState.LoadingDryRun);
            var unloadCmd = MakePrepareStateCmd(DSMState.UnloadingDryRun);

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
        public async Task Abort_DuringLoadingDryRun_DiscardsSnap()
        {
            var handler = new DryRunDsmHandler(_liveRepo);
            var loadCmd = MakePrepareStateCmd(DSMState.LoadingDryRun);

            await handler.PrepareAsync(loadCmd, default);
            handler.Commit(loadCmd, _liveRepo);

            Assert.NotNull(handler.TestHook_Snap);

            handler.Abort(loadCmd, _liveRepo);

            Assert.Null(handler.TestHook_Snap);
        }

        // ── S0309-T5: Non-dry-run PrepareState targets are no-ops ────────────

        /// <summary>
        /// A <see cref="NodeOpType.PrepareState"/> targeting any state other than
        /// LoadingDryRun or UnloadingDryRun must not mutate the snapshot or live repo.
        /// </summary>
        [Fact]
        public async Task OtherPrepareStateTargets_AreNoOps()
        {
            var handler = new DryRunDsmHandler(_liveRepo);

            foreach (var state in new[]
            {
                DSMState.Standby, DSMState.LoadingEdit, DSMState.RunningEdit,
                DSMState.LoadingLive, DSMState.RunningLive, DSMState.LoadingReplay,
            })
            {
                var cmd = MakePrepareStateCmd(state);
                await handler.PrepareAsync(cmd, default);
                handler.Commit(cmd, _liveRepo);

                Assert.Null(handler.TestHook_Snap);
            }
        }

        // ── S0309-T6: UnloadingDryRun with no snapshot logs warning silently ──

        /// <summary>
        /// Calling UnloadingDryRun when no snapshot is held (e.g. a missed LoadingDryRun)
        /// must not throw; it should log a warning and return without mutating state.
        /// </summary>
        [Fact]
        public async Task UnloadingDryRun_WithNullSnap_LogsWarningAndReturns()
        {
            var e = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(e, new DryRunTestPos { X = 5f, Y = 5f, Z = 5f });

            var handler   = new DryRunDsmHandler(_liveRepo);
            var unloadCmd = MakePrepareStateCmd(DSMState.UnloadingDryRun);

            // No LoadingDryRun was committed — snapshot is null.
            var ex = await Record.ExceptionAsync(async () =>
            {
                await handler.PrepareAsync(unloadCmd, default);
                handler.Commit(unloadCmd, _liveRepo);
            });

            Assert.Null(ex);

            // Live repo must be unchanged.
            var pos = _liveRepo.GetComponent<DryRunTestPos>(e);
            Assert.Equal(5f, pos.X);
        }
    }
}
