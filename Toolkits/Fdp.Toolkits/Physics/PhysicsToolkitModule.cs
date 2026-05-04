using System;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Physics
{
    /// <summary>
    /// Entry-point module for the Physics toolkit.
    /// Call <see cref="Initialize"/> once (before the first simulation tick) to allocate the
    /// <see cref="RaycastBatchData"/> singleton and register it on the world.
    /// <para>
    /// <b>Memory ownership:</b> <see cref="Initialize"/> allocates two
    /// <see cref="Fdp.Core.Collections.NativeArray{T}"/> arrays with
    /// <see cref="Allocator.Persistent"/> and registers them as a world singleton.
    /// The module retains the array handles so that <see cref="Dispose"/> can free
    /// them when the simulation ends.  Scenarios must keep the module alive for the
    /// entire simulation lifetime and call <see cref="Dispose"/> (or implement
    /// <see cref="Fdp.Examples.Common.IScenario.OnShutdown"/>) as the last step
    /// <em>after</em> the world is disposed.
    /// </para>
    /// </summary>
    public sealed class PhysicsToolkitModule : IDisposable
    {
        // Retained for the lifetime of the simulation so Dispose() can free them.
        private RaycastBatchData _batchData;
        private bool _initialized;
        private bool _disposed;

        /// <summary>
        /// Allocates persistent native arrays, constructs the <see cref="RaycastBatchData"/>
        /// singleton, and registers it on <paramref name="world"/>.
        /// The module retains the native-array handles; call <see cref="Dispose"/> after
        /// the simulation ends to release them.
        /// Must be called exactly once before simulation starts.
        /// </summary>
        public void Initialize(EntityRepository world)
        {
            if (_initialized)
                throw new InvalidOperationException("PhysicsToolkitModule.Initialize called more than once.");

            _batchData = new RaycastBatchData
            {
                Hits = new NativeArray<RaycastHit>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
            };

            // SetSingleton copies the struct value (including native array pointers) into the world.
            // The module keeps its own copy so Dispose() can free the arrays at shutdown.
            world.SetSingleton(_batchData);

            _initialized = true;
        }

        /// <summary>
        /// Disposes the persistent <see cref="NativeArray{T}"/> backing the
        /// <see cref="RaycastBatchData"/> singleton.
        /// Must be called after the simulation world is shut down.
        /// Safe to call multiple times (guarded by <c>_disposed</c>).
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_batchData.Hits.IsCreated) _batchData.Hits.Dispose();
        }
    }
}
