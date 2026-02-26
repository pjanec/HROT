using System;
using System.Collections.Generic;
using Fdp.Kernel;
using ModuleHost.Core.Network.Interfaces;

namespace FDP.Toolkit.Replication.Services
{
    [ComponentId(GlobalComponentIds.BlockIdManager)]
    public class BlockIdManager : INetworkIdAllocator
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

        public void Dispose()
        {
            _localPool.Clear();
            OnLowWaterMark = null;
        }
        
        // Helper for testing
        public int AvailableCount => _localPool.Count;
    }
}
