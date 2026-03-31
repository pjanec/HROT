using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel.FlightRecorder;
using K4os.Compression.LZ4;

namespace Fdp.Kernel.Orchestration
{
    /// <summary>
    /// Background-thread checkpoint I/O worker — implements Step 3 of the 3-step binary
    /// checkpointing protocol (CGF1-S0303).
    ///
    /// <para>
    /// <b>Protocol:</b>
    /// <list type="number">
    ///   <item>Step 1 (caller): Publish <c>NodeOpStatus(InProgress)</c> immediately.</item>
    ///   <item>Step 2 (caller, main thread): Call <c>snap.SyncFrom(liveRepo)</c> (~2 ms
    ///     unmanaged memcpy) then call <see cref="Enqueue"/>.</item>
    ///   <item>Step 3 (this class, background thread): LZ4-compress the snapshot and write
    ///     to <c>{storageDir}/{requestId}_node_{nodeId}.fdp</c>. Sets
    ///     <c>CompletionResults[requestId]</c> when done. The caller's <c>ClusterSlave.Tick()</c>
    ///     polls <see cref="TakeCompletedResults"/> each frame and publishes the deferred
    ///     <c>NodeOpStatus(Success/Failure)</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// The dedicated background <see cref="Thread"/> (not a <c>Task</c>) prevents thread-pool
    /// starvation when many checkpoints are queued concurrently. Items are processed one at
    /// a time to avoid CPU-cache thrashing and disk contention.
    /// </para>
    ///
    /// <para>
    /// Serialization reuses <see cref="RecorderSystem.RecordKeyframe"/> so the on-disk format
    /// is identical to a flight-recorder keyframe (DRY: shared unmanaged chunk copy,
    /// sanitization, and LZ4 compression pipeline).
    /// </para>
    /// </summary>
    public sealed class CheckpointIOWorker : IDisposable
    {
        private const int SerializeBufferSize = 32 * 1024 * 1024; // 32 MB

        private readonly struct WorkItem
        {
            public readonly EntityRepository Snapshot;
            public readonly Guid             RequestId;
            public WorkItem(EntityRepository snapshot, Guid requestId)
            {
                Snapshot  = snapshot;
                RequestId = requestId;
            }
        }

        private readonly ConcurrentQueue<WorkItem>          _queue           = new();
        private readonly ConcurrentDictionary<Guid, bool>   _results         = new();
        private readonly string                              _storageDir;
        private readonly int                                 _nodeId;
        private readonly Thread                              _workerThread;
        private readonly CancellationTokenSource             _cts             = new();
        private readonly SemaphoreSlim                       _workAvailable   = new(0);

        // Pre-allocated serialization buffers (used exclusively by worker thread).
        private readonly byte[] _rawBuffer;
        private readonly byte[] _compressedBuffer;
        private readonly RecorderSystem _recorderSystem = new();

        // Counts items in queue + currently being processed.
        private volatile int _pendingCount = 0;

        private bool _disposed;

        /// <summary>
        /// Initialises the worker and starts the background drain thread.
        /// </summary>
        /// <param name="storageDir">
        /// Directory where checkpoint files are written.
        /// Created automatically if it does not exist.
        /// </param>
        /// <param name="nodeId">
        /// Node identifier embedded in output filenames:
        /// <c>{requestId}_node_{nodeId}.fdp</c>.
        /// </param>
        public CheckpointIOWorker(string storageDir, int nodeId)
        {
            _storageDir = storageDir ?? throw new ArgumentNullException(nameof(storageDir));
            _nodeId     = nodeId;

            Directory.CreateDirectory(storageDir);

            _rawBuffer        = new byte[SerializeBufferSize];
            _compressedBuffer = new byte[LZ4Codec.MaximumOutputSize(SerializeBufferSize)];

            _workerThread = new Thread(RunWorkerLoop)
            {
                IsBackground = true,
                Name         = $"CheckpointIOWorker-Node{nodeId}",
            };
            _workerThread.Start();
        }

        /// <summary>
        /// Enqueues an <see cref="EntityRepository"/> snapshot for async LZ4-compressed
        /// write to disk. The snapshot is owned by the worker after this call: it will
        /// be disposed after the write completes (success or failure).
        /// </summary>
        /// <param name="snapshot">Fully-synced snapshot repository (caller must not mutate after this call).</param>
        /// <param name="requestId">Request identifier (used in filename and completion key).</param>
        public void Enqueue(EntityRepository snapshot, Guid requestId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CheckpointIOWorker));
            Interlocked.Increment(ref _pendingCount);
            _queue.Enqueue(new WorkItem(snapshot, requestId));
            _workAvailable.Release();
        }

        /// <summary>
        /// Returns a task that completes only when the queue is empty <em>and</em> the
        /// background thread has finished processing the last item (i.e. all files are
        /// physically on disk). Safe to <c>await</c> from any thread.
        /// </summary>
        public async Task DrainAsync()
        {
            while (Volatile.Read(ref _pendingCount) > 0)
                await Task.Delay(5).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes and returns all completed checkpoint results accumulated since the
        /// last call.  Each entry maps a <c>requestId</c> to <c>true</c> (success) or
        /// <c>false</c> (failure).  Call from <c>ClusterSlave.Tick()</c> each frame.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<(Guid RequestId, bool Success)> TakeCompletedResults()
        {
            var results = new System.Collections.Generic.List<(Guid, bool)>();
            foreach (var kv in _results)
                if (_results.TryRemove(kv.Key, out var success))
                    results.Add((kv.Key, success));
            return results;
        }

        // ── Background worker ─────────────────────────────────────────────────

        private void RunWorkerLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                // Block until work is available (100 ms timeout to check cancellation).
                if (!_workAvailable.Wait(100)) continue;

                if (!_queue.TryDequeue(out var item)) continue;

                bool success = false;
                try
                {
                    WriteCheckpointFile(item.Snapshot, item.RequestId);
                    success = true;
                }
                catch (Exception ex)
                {
                    // Failure recorded; ClusterSlave will publish NodeOpStatus.Failure.
                    _ = ex;
                }
                finally
                {
                    // Dispose the snapshot — ownership transferred to worker.
                    try { item.Snapshot.Dispose(); } catch { /* best effort */ }

                    _results[item.RequestId] = success;
                    Interlocked.Decrement(ref _pendingCount);
                }
            }
        }

        /// <summary>
        /// Serializes <paramref name="snapshot"/> as an LZ4-compressed keyframe using
        /// the same <see cref="RecorderSystem.RecordKeyframe"/> pipeline as the flight
        /// recorder (DRY reuse of the unmanaged chunk-copy + sanitization path).
        /// </summary>
        private void WriteCheckpointFile(EntityRepository snapshot, Guid requestId)
        {
            var filePath = Path.Combine(_storageDir, $"{requestId}_node_{_nodeId}.fdp");

            // ── Serialize to raw buffer ──────────────────────────────────────
            int rawBytes;
            using (var ms = new MemoryStream(_rawBuffer))
            using (var bw = new BinaryWriter(ms))
            {
                _recorderSystem.RecordKeyframe(snapshot, bw, DateTimeOffset.UtcNow.Ticks);
                bw.Flush();
                rawBytes = (int)ms.Position;
            }

            // ── LZ4-compress ─────────────────────────────────────────────────
            int compressedBytes = LZ4Codec.Encode(
                _rawBuffer, 0, rawBytes,
                _compressedBuffer, 0, _compressedBuffer.Length);

            // ── Write file: [magic:4][uncompressedSize:4][compressedSize:4][payload] ──
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var fw = new BinaryWriter(fs);
            fw.Write(0x46445043);   // 'FDPC' magic (FDP Checkpoint)
            fw.Write(rawBytes);
            fw.Write(compressedBytes);
            fw.Write(_compressedBuffer, 0, compressedBytes);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _workAvailable.Release(); // Unblock any blocking Wait
            _workerThread.Join(TimeSpan.FromSeconds(5));
            _cts.Dispose();
            _workAvailable.Dispose();
        }
    }
}
