using System;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Orchestration;
using K4os.Compression.LZ4;
using Xunit;

namespace Fdp.Tests
{
    // ── Test component for checkpoint round-trips ───────────────────────────
    [ComponentId(205)]
    internal struct CheckpointTestPos
    {
        public float X, Y, Z;
    }

    /// <summary>
    /// Tests for <see cref="CheckpointIOWorker"/> (CGF1-S0303 success conditions).
    /// </summary>
    public sealed class CheckpointIOWorkerTests : IDisposable
    {
        private readonly string _storageDir;

        public CheckpointIOWorkerTests()
        {
            _storageDir = Path.Combine(Path.GetTempPath(), "fdp_ckpt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_storageDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_storageDir))
                Directory.Delete(_storageDir, recursive: true);
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static EntityRepository MakeRepo(params (float X, float Y, float Z)[] positions)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<CheckpointTestPos>();
            foreach (var (x, y, z) in positions)
            {
                var e = repo.CreateEntity();
                repo.SetComponent(e, new CheckpointTestPos { X = x, Y = y, Z = z });
            }
            return repo;
        }

        // ── CGF1-S0303: DrainAsync_WaitsForQueueEmpty ─────────────────────────

        /// <summary>
        /// Enqueue 3 items; <see cref="CheckpointIOWorker.DrainAsync"/> must not return
        /// until all 3 files exist on disk.
        /// </summary>
        [Fact]
        public async Task DrainAsync_WaitsForQueueEmpty()
        {
            using var worker = new CheckpointIOWorker(_storageDir, nodeId: 1);

            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            foreach (var id in ids)
            {
                var snap = MakeRepo((1f, 2f, 3f));
                worker.Enqueue(snap, id); // worker takes ownership
            }

            await worker.DrainAsync();

            foreach (var id in ids)
            {
                var path = Path.Combine(_storageDir, $"{id}_node_1.fdp");
                Assert.True(File.Exists(path), $"Expected checkpoint file not found: {path}");
            }
        }

        // ── File name convention ───────────────────────────────────────────────

        /// <summary>
        /// Verifies the output file follows the naming convention
        /// <c>{requestId}_node_{nodeId}.fdp</c>.
        /// </summary>
        [Fact]
        public async Task Enqueue_WritesFileWithExpectedName()
        {
            using var worker = new CheckpointIOWorker(_storageDir, nodeId: 7);
            var reqId = Guid.NewGuid();

            var snap = MakeRepo((10f, 20f, 30f));
            worker.Enqueue(snap, reqId);

            await worker.DrainAsync();

            var expectedPath = Path.Combine(_storageDir, $"{reqId}_node_7.fdp");
            Assert.True(File.Exists(expectedPath));
            Assert.True(new FileInfo(expectedPath).Length > 0, "Checkpoint file must not be empty.");
        }

        // ── TakeCompletedResults returns Success ───────────────────────────────

        /// <summary>
        /// After DrainAsync, <see cref="CheckpointIOWorker.TakeCompletedResults"/> must
        /// return <c>true</c> for the completed request.
        /// </summary>
        [Fact]
        public async Task TakeCompletedResults_ReportsSuccess_AfterWrite()
        {
            using var worker = new CheckpointIOWorker(_storageDir, nodeId: 2);
            var reqId = Guid.NewGuid();

            var snap = MakeRepo((5f, 6f, 7f));
            worker.Enqueue(snap, reqId);

            await worker.DrainAsync();

            var results = worker.TakeCompletedResults();
            Assert.Single(results);
            Assert.Equal(reqId, results[0].RequestId);
            Assert.True(results[0].Success);
        }

        // ── TakeCompletedResults is cleared after taking ───────────────────────

        /// <summary>
        /// A second call to <see cref="CheckpointIOWorker.TakeCompletedResults"/> must
        /// return an empty list (results are consumed on first take).
        /// </summary>
        [Fact]
        public async Task TakeCompletedResults_EmptyOnSecondCall()
        {
            using var worker = new CheckpointIOWorker(_storageDir, nodeId: 3);
            var reqId = Guid.NewGuid();

            var snap = MakeRepo((0f, 0f, 0f));
            worker.Enqueue(snap, reqId);
            await worker.DrainAsync();

            _ = worker.TakeCompletedResults(); // drain
            var second = worker.TakeCompletedResults();
            Assert.Empty(second);
        }

        // ── S504-T1: Checkpoint file contains event from current bus buffer ───

        /// <summary>
        /// Verifies that when a snapshot's event bus current buffer holds an event,
        /// the written checkpoint file encodes at least one unmanaged event stream
        /// (unmanagedStreamCount &gt;= 1 in the decompressed payload).
        /// </summary>
        [Fact]
        public async Task Enqueue_RepoWithCurrentBufferEvent_CheckpointContainsEventStream()
        {
            using var worker = new CheckpointIOWorker(_storageDir, nodeId: 50);
            var reqId = Guid.NewGuid();

            var snap = MakeRepo((1f, 2f, 3f));

            // Inject a synthetic event directly into the current (read) bus buffer.
            // typeId 30001 and elementSize 4 are arbitrary test values.
            snap.Bus.InjectIntoCurrentBySize(30001, 4, new byte[] { 0x01, 0x02, 0x03, 0x04 });

            worker.Enqueue(snap, reqId);
            await worker.DrainAsync();

            // Read file: [magic:4][rawSize:4][compressedSize:4][LZ4 payload]
            var filePath = Path.Combine(_storageDir, $"{reqId}_node_50.fdp");
            byte[] fileBytes = File.ReadAllBytes(filePath);

            // Parse header
            int rawSize        = BitConverter.ToInt32(fileBytes, 4);
            int compressedSize = BitConverter.ToInt32(fileBytes, 8);
            const int HeaderBytes = 12;

            // Decompress payload
            byte[] raw = new byte[rawSize];
            int decoded = LZ4Codec.Decode(fileBytes, HeaderBytes, compressedSize, raw, 0, rawSize);
            Assert.Equal(rawSize, decoded);

            // Parse RecordKeyframe binary layout:
            //   8  GlobalVersion (ulong)
            //   1  FrameType (byte)
            //   8  WallClockTicks (long)
            //   4  DestroyCount (int)
            //  --- WriteEvents ---
            //   4  unmanagedStreamCount (int)  <-- offset 21
            const int UnmanagedCountOffset = 8 + 1 + 8 + 4;
            int unmanagedStreamCount = BitConverter.ToInt32(raw, UnmanagedCountOffset);

            Assert.True(unmanagedStreamCount >= 1,
                $"Expected at least 1 unmanaged stream; got {unmanagedStreamCount}");
        }
    }
}
