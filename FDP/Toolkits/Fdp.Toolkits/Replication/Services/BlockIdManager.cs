using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;

namespace Fdp.Toolkit.Replication.Services
{
    [ComponentId(GlobalComponentIds.BlockIdManager)]
    public class BlockIdManager : INetworkIdAllocator, IRestorableIdAllocator
    {
        private readonly Queue<long> _localPool = new();
        private readonly int _lowWaterMark;
        
        /// <summary>
        /// Triggered when the pool size drops at or below the low water mark.
        /// Consumers should respond by requesting a new block and calling AddBlock.
        /// </summary>
        public event Action? OnLowWaterMark;

        public BlockIdManager(int lowWaterMark = 10)
        {
            _lowWaterMark = lowWaterMark;
        }

        public long AllocateId()
        {
            // Check if we are at or will drop below low water mark
            if (_localPool.Count <= _lowWaterMark + 1)
            {
                OnLowWaterMark?.Invoke();
            }

            if (_localPool.Count == 0)
            {
                throw new InvalidOperationException("ID Pool exhausted. Make sure to handle OnLowWaterMark and call AddBlock.");
            }

            return _localPool.Dequeue();
        }

        public void AddBlock(long start, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            for (int i = 0; i < count; i++)
            {
                _localPool.Enqueue(start + i);
            }
        }

        public void Reset(long startId = 0)
        {
            _localPool.Clear();
            // Optional: If startId > 0, we could potentially seed it, 
            // but for a BlockManager, we typically wait for a block.
            // If the semantics of Reset(X) mean "Start allocating from X immediately", 
            // we might need to pretend we have a block [X, X+1...].
            // But usually Reset is global.
            // For now, Clear is safe.
        }

        // ── Preview dry-run: restore the pool we HELD, never the central authority ────────────

        /// <inheritdoc/>
        /// <remarks>
        /// ⭐⭐⭐ 🔒 <b>The user's model, `2026-08-23`:</b> <i>"each node needs to remember the ids/chunks used
        /// during the run and on world reset to simply reset to their beginning while the central allocatore
        /// stays where it is."</i> ⇒ ⭐ for a pooled allocator the "issuing position" IS the queue of ids it
        /// already holds, and the capture is that queue.
        ///
        /// <para>⛔⛔ <b>Nothing here contacts a central authority</b> — 📌 that is the whole point.
        /// <see cref="Reset"/> would clear the pool and *(on the DDS sibling)* broadcast a global reset;
        /// this puts back exactly what this node had.</para>
        ///
        /// <para>⚠ <b>The boundary, stated:</b> restoring re-offers the ids held at capture. Ids this node
        /// obtained from a NEW block mid-preview are NOT re-offered — the central authority advanced past
        /// them and may have given that range to someone else. ⇒ ids repeat exactly while a preview stays
        /// within the pool it started with; beyond that the prefix repeats and the tail differs.</para>
        ///
        /// <para>⛔ <c>null</c> when the pool is empty: there is no position to promise.</para>
        /// </remarks>
        public object? CaptureIssuingPosition()
            => _localPool.Count == 0 ? null : _localPool.ToArray();

        /// <inheritdoc/>
        public void RestoreIssuingPosition(object snapshot)
        {
            if (snapshot is not long[] held) return;

            // ⭐ Replace, do not prepend-and-keep: anything acquired during the preview is spent from the
            //   cluster's point of view, and re-offering it is a cross-node collision.
            _localPool.Clear();
            foreach (var id in held) _localPool.Enqueue(id);
        }

        public void Dispose()
        {
            _localPool.Clear();
            OnLowWaterMark = null;
        }
        
        // Helper for testing
        public int AvailableCount => _localPool.Count;
    }
}
