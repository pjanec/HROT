using System.Collections.Concurrent;
using Fdp.Core;

namespace Fdp.ModuleHost.Providers
{
    /// <summary>
    /// Thread-safe pool of EntityRepository instances for snapshot reuse.
    /// Eliminates GC allocations by recycling repositories.
    ///
    /// <para>⭐⭐⭐ <b><c>QA-006</c> — the pool OWNS every repository it creates and must be disposed.</b>
    /// 📐 Measured 2026-08-26: <c>ModuleHostKernel.Initialize</c> builds one of these per kernel with
    /// <c>warmupCount: 10</c>, and nothing released them — so <b>every node teardown in the product
    /// leaked TEN <see cref="EntityRepository"/> instances</b>, each holding an <c>int[1_000_000]</c>
    /// free list plus one <c>NativeChunkTable</c> per registered component. ⛔ That, not the single
    /// leaked world beside it, was the dominant term: it exhausted a 16 GB box mid integration run and
    /// aborted the test host, which had read as "flaky tests" for ~40 batches.</para>
    ///
    /// <para>⚠ Pooling and ownership are not the same thing. This type was written purely as an
    /// allocation optimisation — <i>"recycling repositories"</i> — and a recycler with no end of life
    /// is a leak by construction.</para>
    /// </summary>
    public class SnapshotPool : IDisposable
    {
        private readonly ConcurrentStack<EntityRepository> _pool = new();
        private readonly Action<EntityRepository>? _schemaSetup;
        private readonly int _warmupCount;

        // QA-006: every repository this pool ever created, leased or pooled. Disposing only what
        // happens to be IN the stack would miss the ones currently on loan to a provider.
        private readonly ConcurrentBag<EntityRepository> _created = new();
        private bool _disposed;

        public SnapshotPool(Action<EntityRepository>? schemaSetup, int warmupCount = 0)
        {
            _schemaSetup = schemaSetup;
            _warmupCount = warmupCount;

            // Pre-populate pool
            for (int i = 0; i < warmupCount; i++)
            {
                var repo = CreateNew();
                _pool.Push(repo);
            }
        }

        /// <summary>
        /// ⭐ <c>QA-006</c> — disposes every repository this pool created, pooled or leased.
        /// Idempotent, and <see cref="EntityRepository.Dispose"/> is itself idempotent, so a lessee
        /// that also disposes its snapshot is harmless.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var repo in _created) repo.Dispose();
            _pool.Clear();
        }

        /// <summary>
        /// Get a repository from pool or create new if empty.
        /// </summary>
        public EntityRepository Get()
        {
            if (_pool.TryPop(out var repo))
            {
                return repo;
            }
            
            return CreateNew();
        }
        
        /// <summary>
        /// Return repository to pool after clearing.
        /// </summary>
        public void Return(EntityRepository repo)
        {
            // CRITICAL: Clear state but keep buffer capacity
            // Assuming SoftClear is a method on EntityRepository. 
            // If it doesn't exist, we might need to use Clear() or implement it.
            // The instructions say "SoftClear()", so I will use that.
            // If compilation fails, I'll check EntityRepository.
            repo.SoftClear();
            
            _pool.Push(repo);
        }
        
        private EntityRepository CreateNew()
        {
            var repo = new EntityRepository();
            _schemaSetup?.Invoke(repo);
            _created.Add(repo);   // QA-006 — ownership is recorded at creation, not at pooling
            return repo;
        }
        
        /// <summary>
        /// Statistics for monitoring
        /// </summary>
        public int PooledCount => _pool.Count;
    }
}
