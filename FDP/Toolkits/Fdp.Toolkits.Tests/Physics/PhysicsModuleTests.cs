using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PhysicsToolkitModule"/> (BCS-P4-T1).
    /// </summary>
    public class PhysicsModuleTests : IDisposable
    {
        private readonly EntityRepository _world;

        public PhysicsModuleTests()
        {
            _world = new EntityRepository();
        }

        public void Dispose()
        {
            if (_world.HasSingleton<RaycastBatchData>())
            {
                ref var b = ref _world.GetSingleton<RaycastBatchData>();
                if (b.Hits.IsCreated) b.Hits.Dispose();
            }
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="PhysicsToolkitModule.Initialize"/> must create a
        /// <see cref="RaycastBatchData"/> singleton on the world.
        /// </summary>
        [Fact]
        public void PhysicsModule_Initialize_CreatesSingleton()
        {
            var module = new PhysicsToolkitModule();
            module.Initialize(_world);

            Assert.True(_world.HasSingleton<RaycastBatchData>());

            // Initialize transferred ownership to the world singleton.
            // module.Dispose() is a no-op after ownership transfer; this.Dispose() cleans up
            // the arrays via the world singleton (Requests.Dispose + Hits.Dispose).
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// After <see cref="PhysicsToolkitModule.Initialize"/>, the Hits array in the singleton
        /// must be sized to <see cref="PhysicsConstants.RaycastBatchCapacity"/> (4096).
        /// </summary>
        [Fact]
        public void RaycastBatchData_Capacity_Is4096()
        {
            // 'var' not 'using var': Initialize() transferred ownership to the world singleton;
            // module.Dispose() is a no-op.  this.Dispose() handles array cleanup.
            var module = new PhysicsToolkitModule();
            module.Initialize(_world);

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();

            Assert.Equal(PhysicsConstants.RaycastBatchCapacity, batch.Hits.Length);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="PhysicsCollider"/> must be an unmanaged value type with
        /// <c>sizeof == 12</c> (<c>Radius</c>, <c>CollisionLayer</c>, <c>Height</c>).
        /// ⭐ Was 8 until <c>Height</c> was added for 3-D line of sight (docs/DESIGN_Terrain_World.md §4.3,
        /// 2026-10-03) — a deliberate layout change: no wire topic carries the struct, and scenario JSON names its
        /// fields, so an older file loads with <c>Height = 0</c> (unknown ⇒ blocks at every height, as before).
        /// </summary>
        [Fact]
        public void PhysicsCollider_IsUnmanagedValueType()
        {
            Assert.True(typeof(PhysicsCollider).IsValueType);
            Assert.Equal(12, Marshal.SizeOf<PhysicsCollider>());
        }
    }
}
