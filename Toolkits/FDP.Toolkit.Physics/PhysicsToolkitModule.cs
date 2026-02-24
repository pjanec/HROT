using System;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Physics.Components;

namespace FDP.Toolkit.Physics
{
    /// <summary>
    /// Entry-point module for the Physics toolkit.
    /// Call <see cref="Initialize"/> once (before the first simulation tick) to allocate the
    /// <see cref="RaycastBatchData"/> singleton and register it on the world.
    /// <para>
    /// <b>Memory ownership:</b> <see cref="Initialize"/> allocates two
    /// <see cref="Fdp.Kernel.Collections.NativeArray{T}"/> arrays with
    /// <see cref="Allocator.Persistent"/>, then calls <c>world.SetSingleton</c> to make them
    /// reachable through the world.  After <c>SetSingleton</c> returns the module clears its own
    /// handles — the world caller becomes the sole owner and must free the arrays by calling
    /// <c>batch.Requests.Dispose(); batch.Hits.Dispose()</c> on the singleton at shutdown.
    /// </para>
    /// <para>
    /// <see cref="Dispose"/> on this module is a no-op after <see cref="Initialize"/> completes
    /// (ownership was transferred).  It only disposes if <see cref="Initialize"/> was never called
    /// and arrays were somehow partially allocated (defensive guard for future code changes).
    /// </para>
    /// </summary>
    public sealed class PhysicsToolkitModule : IDisposable
    {
        // Retained only until SetSingleton completes; cleared immediately after.
        private RaycastBatchData _batchData;
        private bool _initialized;
        private bool _disposed;

        /// <summary>
        /// Allocates persistent native arrays, constructs the <see cref="RaycastBatchData"/>
        /// singleton, registers it on <paramref name="world"/>, then <em>transfers ownership</em>
        /// to the world — the module's local array handles are cleared so that a subsequent
        /// <see cref="Dispose"/> call is a safe no-op.
        /// Must be called exactly once before simulation starts.
        /// </summary>
        public void Initialize(EntityRepository world)
        {
            if (_initialized)
                throw new InvalidOperationException("PhysicsToolkitModule.Initialize called more than once.");

            _batchData = new RaycastBatchData
            {
                Requests = new NativeArray<RaycastRequest>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
                Hits     = new NativeArray<RaycastHit>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
                Count    = 0,
            };

            // SetSingleton copies the struct value (including native array pointers) into the world.
            world.SetSingleton(_batchData);

            // ── Ownership transfer ────────────────────────────────────────────────────────────
            // The world singleton now holds the only authoritative copy of these array handles.
            // Clear the module's local copies so that Dispose() below is a safe no-op and the
            // caller cannot accidentally free the same native memory twice.
            _batchData = default;   // NativeArray.IsCreated = false on default

            _initialized = true;
        }

        /// <summary>
        /// No-op after a successful <see cref="Initialize"/> call (ownership was transferred to
        /// the world).  Exists for defensive completeness and <c>using</c>-statement compatibility.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // _batchData was cleared in Initialize; IsCreated will be false — safe no-op.
            if (_batchData.Requests.IsCreated) _batchData.Requests.Dispose();
            if (_batchData.Hits.IsCreated)     _batchData.Hits.Dispose();
        }
    }
}
