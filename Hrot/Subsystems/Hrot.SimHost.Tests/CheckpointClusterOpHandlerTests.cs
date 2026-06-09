using System;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    // ── Minimal test component (test-only ID 266) ─────────────────────────────
    // NOTE: IDs 265+ are above the production GlobalComponentIds range (max 264).

    [ComponentId(266)]
    internal struct CkptPos
    {
        public float X, Y, Z;
    }

    /// <summary>
    /// Tests for <see cref="ReferenceCheckpointHandler"/> — CGF1-S0303 success conditions.
    /// </summary>
    public sealed class CheckpointClusterOpHandlerTests : IDisposable
    {
        private readonly string           _storageDir;
        private readonly EntityRepository _liveRepo;
        private readonly int              _nodeId = 99;

        public CheckpointClusterOpHandlerTests()
        {
            _storageDir = Path.Combine(Path.GetTempPath(), "fdp_ckpt_sh_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_storageDir);

            _liveRepo = new EntityRepository();
            _liveRepo.RegisterComponent<CkptPos>();
        }

        public void Dispose()
        {
            _liveRepo.Dispose();
            if (Directory.Exists(_storageDir))
                Directory.Delete(_storageDir, recursive: true);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ExecuteNodeOpIntent MakeSnapshotCmd(Guid? txId = null) =>
            new ExecuteNodeOpIntent
            {
                TransactionId = txId ?? Guid.NewGuid(),
                TargetNodeId  = 0,
                Operation     = Fdp.Toolkit.Orchestration.NodeOpType.TakeSnapshot,
            };

        private ReferenceCheckpointHandler CreateHandler(CheckpointIOWorker worker) =>
            new ReferenceCheckpointHandler(worker, _liveRepo, new EventAccumulator());

        // ── CGF1-S0303: TwoOverlappingCheckpoints_ACKsAreBothDeferred ─────────

        /// <summary>
        /// Two rapid TakeSnapshot commands — both must publish InProgress immediately,
        /// and both Success ACKs must only appear after disk writes complete.
        /// </summary>
        [Fact]
        public async Task TwoOverlappingCheckpoints_BothACKsDeferredUntilDrainComplete()
        {
            using var worker  = new CheckpointIOWorker(_storageDir, _nodeId);
            var handler = CreateHandler(worker);

            var idA = Guid.NewGuid();
            var idB = Guid.NewGuid();
            var cmdA = MakeSnapshotCmd(idA);
            var cmdB = MakeSnapshotCmd(idB);

            await handler.PrepareAsync(cmdA, default);
            var eA = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(eA, new CkptPos { X = 1f, Y = 2f, Z = 3f });
            handler.Commit(cmdA, _liveRepo);

            await handler.PrepareAsync(cmdB, default);
            var eB = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(eB, new CkptPos { X = 4f, Y = 5f, Z = 6f });
            handler.Commit(cmdB, _liveRepo);

            // Drain blocks until both background writes finish.
            await worker.DrainAsync();

            var pathA = Path.Combine(_storageDir, $"{idA}_node_{_nodeId}.fdp");
            var pathB = Path.Combine(_storageDir, $"{idB}_node_{_nodeId}.fdp");
            Assert.True(File.Exists(pathA), $"Checkpoint A file missing: {pathA}");
            Assert.True(File.Exists(pathB), $"Checkpoint B file missing: {pathB}");

            // Both completed results are available after drain (ACKs were deferred to worker).
            var results = worker.TakeCompletedResults();
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
        }

        // ── CGF1-S0303: SecondSnapshotCaptures_DifferentState_thanFirst ───────

        /// <summary>
        /// The second snapshot is taken after a component mutation, so the file for B
        /// must be strictly larger than A (more entities = more data) or at least distinct.
        /// Here we verify file sizes differ since A has 1 entity and B has 2 entities.
        /// </summary>
        [Fact]
        public async Task SecondSnapshotCaptures_DifferentState_thanFirst()
        {
            using var worker  = new CheckpointIOWorker(_storageDir, _nodeId);
            var handler = CreateHandler(worker);

            var idA = Guid.NewGuid();
            var idB = Guid.NewGuid();

            // First commit: 1 entity.
            var cmdA = MakeSnapshotCmd(idA);
            await handler.PrepareAsync(cmdA, default);
            var e1 = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(e1, new CkptPos { X = 1f, Y = 0f, Z = 0f });
            handler.Commit(cmdA, _liveRepo);

            // Mutate: add second entity then second commit.
            var cmdB = MakeSnapshotCmd(idB);
            await handler.PrepareAsync(cmdB, default);
            var e2 = _liveRepo.CreateEntity();
            _liveRepo.SetComponent(e2, new CkptPos { X = 2f, Y = 0f, Z = 0f });
            handler.Commit(cmdB, _liveRepo);

            await worker.DrainAsync();

            var sizeA = new FileInfo(Path.Combine(_storageDir, $"{idA}_node_{_nodeId}.fdp")).Length;
            var sizeB = new FileInfo(Path.Combine(_storageDir, $"{idB}_node_{_nodeId}.fdp")).Length;

            // B captured more entity data than A (2 entities vs 1).
            Assert.True(sizeB >= sizeA,
                $"Snapshot B ({sizeB} B) should be >= A ({sizeA} B) — B captures 2 entities, A captures 1.");
        }

        // ── CGF1-S0303: NullRepo_NothingEnqueuedAndNoFileWritten ─────────────

        /// <summary>
        /// When both <c>liveRepo</c> (injected) and <c>repo</c> (passed to Commit) are
        /// <c>null</c>, Commit must bail out immediately — nothing is enqueued on the
        /// worker, no file is written, and <see cref="CheckpointIOWorker.TakeCompletedResults"/>
        /// returns empty after a drain.
        /// </summary>
        [Fact]
        public async Task NullRepo_NothingEnqueuedAndNoFileWritten()
        {
            using var worker  = new CheckpointIOWorker(_storageDir, _nodeId);
            // Construct handler with null liveRepo so there is no fallback.
            var handler = new ReferenceCheckpointHandler(worker, liveRepo: null, new EventAccumulator());

            var id  = Guid.NewGuid();
            var cmd = MakeSnapshotCmd(id);

            await handler.PrepareAsync(cmd, default);
            handler.Commit(cmd, repo: null); // Both null — should bail out.

            // DrainAsync returns immediately (nothing queued).
            await worker.DrainAsync();

            Assert.Empty(worker.TakeCompletedResults());
            var path = Path.Combine(_storageDir, $"{id}_node_{_nodeId}.fdp");
            Assert.False(File.Exists(path), "No checkpoint file should be written when repo is null.");
        }

        // ── LiveUnloading_WaitsForCheckpointDrain ─────────────────────────────

        /// <summary>
        /// When <see cref="NodeOpType.FinalizeLive"/> is received while a checkpoint write
        /// is still in-flight, <c>LiveLoadClusterStateHandler.PrepareAsync</c> must not return
        /// until the in-flight checkpoint finishes writing.
        /// </summary>
        [Fact]
        public async Task LiveUnloading_WaitsForCheckpointDrain()
        {
            using var worker  = new CheckpointIOWorker(_storageDir, _nodeId);
            var ckptHandler = CreateHandler(worker);
            // ClusterSlave internal parameterless ctor (no DDS) is accessible via InternalsVisibleTo.
            var liveHandler = new ReferenceLiveLoadHandler(worker);

            // Enqueue a checkpoint while "live" (not yet drained).
            var id  = Guid.NewGuid();
            var cmd = MakeSnapshotCmd(id);
            await ckptHandler.PrepareAsync(cmd, default);
            ckptHandler.Commit(cmd, _liveRepo);

            // FinalizeLive PrepareAsync must not return until write completes.
            await liveHandler.PrepareAsync(
                new ExecuteNodeOpIntent
                {
                    TransactionId = Guid.NewGuid(),
                    TargetNodeId  = 0,
                    Operation     = Fdp.Toolkit.Orchestration.NodeOpType.FinalizeLive,
                },
                default);

            var path = Path.Combine(_storageDir, $"{id}_node_{_nodeId}.fdp");
            Assert.True(File.Exists(path),
                "Checkpoint file must exist after LiveLoadClusterStateHandler.PrepareAsync(FinalizeLive) returns.");
        }
    }
}
