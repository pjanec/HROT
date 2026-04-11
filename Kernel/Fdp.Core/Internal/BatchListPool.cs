using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Fdp.Kernel.Internal
{
    /// <summary>
    /// Thread-safe object pool for batch lists used in parallel entity iteration.
    /// Prevents GC pressure by reusing List instances across frames.
    /// Without pooling, every ForEachParallel call allocates a new List,
    /// causing Gen0 collections and micro-stutters at high frame rates.
    /// </summary>
    internal static class BatchListPool
    {
        private static readonly ConcurrentQueue<List<(int, int)>> _pool = new();

        /// <summary>
        /// Gets a batch list from the pool, or creates a new one if none available.
        /// The returned list is cleared and ready to use.
        /// </summary>
        public static List<(int, int)> Get()
        {
            if (_pool.TryDequeue(out var list))
            {
                list.Clear();
                return list;
            }
            
            // Allocate with decent initial capacity to avoid early resizes.
            // 128 batches is reasonable for most scenarios (e.g., 100K entities with 1024 batch size).
            return new List<(int, int)>(128); 
        }

        /// <summary>
        /// Returns a batch list to the pool for reuse.
        /// The list should not be used after being returned.
        /// </summary>
        public static void Return(List<(int, int)> list)
        {
            if (list != null)
            {
                _pool.Enqueue(list);
            }
        }
    }
}
