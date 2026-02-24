using CarKinem.Spatial;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
// BATCH-10: HitEvent moved to Fdp.Kernel — using FDP.Toolkit.Combat.Events removed.
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Perception.Events;

namespace FDP.Toolkit.Physics.Tests
{
    /// <summary>
    /// Creates a fully-registered <see cref="EntityRepository"/> for Physics toolkit unit tests.
    /// Registers all components and events consumed by the Physics toolkit systems.
    /// Allocates a <see cref="RaycastBatchData"/> singleton with Persistent native arrays.
    /// </summary>
    /// <remarks>
    /// <b>Native memory ownership:</b> Tests that use this factory are responsible for
    /// disposing the native arrays allocated for the singleton.  Preferred pattern:
    /// <code>
    /// var world = PhysicsTestWorldFactory.Create();
    /// try { /* test */ }
    /// finally
    /// {
    ///     ref var b = ref world.GetSingleton&lt;RaycastBatchData&gt;();
    ///     b.Requests.Dispose();
    ///     b.Hits.Dispose();
    /// }
    /// </code>
    /// Test classes that implement <see cref="System.IDisposable"/> may centralise this in
    /// their <c>Dispose</c> method.
    /// </remarks>
    public static class PhysicsTestWorldFactory
    {
        public static EntityRepository Create()
        {
            var world = new EntityRepository();

            // Core spatial components used by RaycastSolverSystem.
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();

            // Physics-specific component.
            world.RegisterComponent<PhysicsCollider>();

            // Events exchanged within and across the Physics pipeline.
            // HitEvent was migrated to FDP.Toolkit.Combat in BATCH-09 (DEBT-023),
            // then moved to Fdp.Kernel in BATCH-10 to break the Combat↔Physics circular dep.
            world.RegisterEvent<HitEvent>();
            world.RegisterEvent<TargetVisibleEvent>();

            // Initialize RaycastBatchData singleton with Persistent allocator.
            var batch = new RaycastBatchData
            {
                Requests = new NativeArray<RaycastRequest>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
                Hits     = new NativeArray<RaycastHit>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
                Count    = 0,
            };
            world.SetSingleton(batch);

            return world;
        }

        /// <summary>Disposes the persistent native arrays in the world's <see cref="RaycastBatchData"/> singleton.</summary>
        public static void DisposeBatch(EntityRepository world)
        {
            if (!world.HasSingleton<RaycastBatchData>()) return;
            ref var b = ref world.GetSingleton<RaycastBatchData>();
            if (b.Requests.IsCreated) b.Requests.Dispose();
            if (b.Hits.IsCreated)     b.Hits.Dispose();
        }

        /// <summary>
        /// Creates a small spatial hash grid (100×100 cells, 5 m/cell, 1000 max entities)
        /// suitable for unit tests. Caller is responsible for calling <see cref="SpatialHashGrid.Dispose"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="SpatialHashGrid.Clear"/> is called immediately after creation so that all
        /// cell-head values are initialised to -1 (the "empty" sentinel).  Without this call the
        /// native short array is zero-filled, which the grid interprets as "entity index 0 is
        /// present in every cell", leading to spurious <c>IndexOutOfRangeException</c> errors.
        /// </remarks>
        public static SpatialHashGrid CreateTestGrid()
        {
            var grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
            grid.Clear();   // initialise all heads to -1 (empty sentinel)
            return grid;
        }
    }
}
