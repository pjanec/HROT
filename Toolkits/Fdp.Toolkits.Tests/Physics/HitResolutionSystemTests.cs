using System;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Core
// BATCH-10: HitEvent moved to Fdp.Core — using FDP.Toolkit.Combat.Events removed.
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Fdp.Toolkit.Perception.Events;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HitResolutionSystem"/> (BCS-P4-T4).
    ///
    /// Test pattern (ComponentSystem):
    ///   1. Seed <see cref="RaycastBatchData"/> directly (bypass the solver for isolation).
    ///   2. <c>sys.Run()</c>.
    ///   3. <c>world.Bus.SwapBuffers()</c> to expose events published by the system.
    ///   4. Assert consumed events.
    /// </summary>
    public class HitResolutionSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly HitResolutionSystem _sys;

        public HitResolutionSystemTests()
        {
            _world = PhysicsTestWorldFactory.Create();
            _sys   = new HitResolutionSystem();
        }

        public void Dispose()
        {
            PhysicsTestWorldFactory.DisposeBatch(_world);
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A hit with a LOS RayId (bit 63 = 0) must cause the system to publish a
        /// <see cref="TargetVisibleEvent"/> with the correct observer and target entity handles.
        /// </summary>
        [Fact]
        public void HitResolution_EmitsTargetVisibleEvent_ForLosHit()
        {
            // Arrange
            // Use Entity constructors to build full handles (Index + Generation).
            // HitResolutionSystem reads hit.Observer / hit.Target directly — no world lookup needed.
            var observerEntity = new Entity(10, 1);
            var targetEntity   = new Entity(20, 1);

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Hits[0] = new RaycastHit
            {
                HasHit    = 1,
                RayId     = PhysicsConstants.PackLosRayId(observerEntity.Index, targetEntity.Index),
                Observer  = observerEntity,
                Target    = targetEntity,
                HitEntity = default,
                T         = 0.5f,
            };
            batch.Count = 1;

            // Act
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            // Assert
            var events = _world.Bus.Read<TargetVisibleEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(observerEntity, events[0].Observer);
            Assert.Equal(targetEntity,   events[0].Target);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A hit with a bullet RayId (bit 63 = 1) must cause the system to publish a
        /// <see cref="HitEvent"/> with the correct <c>BulletEntity</c>.
        /// </summary>
        [Fact]
        public void HitResolution_EmitsHitEvent_ForBulletHit()
        {
            // Arrange
            const int bulletIdx = 42;

            var entity = _world.CreateEntity();
            // Create a bullet entity so GetEntityByIndex returns a valid entity.
            var bulletEntity = _world.CreateEntity();

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Hits[0] = new RaycastHit
            {
                HasHit    = 1,
                RayId     = PhysicsConstants.PackBulletRayId(bulletEntity.Index),
                HitEntity = entity,
                T         = 0.3f,
            };
            batch.Count = 1;

            // Act
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            // Assert
            var events = _world.Bus.Read<HitEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(bulletEntity, events[0].BulletEntity);
            Assert.Equal(entity,       events[0].HitEntity);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// After <see cref="HitResolutionSystem.OnUpdate"/> runs, <see cref="RaycastBatchData.Count"/>
        /// must be reset to zero so the next frame starts with a clean batch.
        /// </summary>
        [Fact]
        public void HitResolution_ClearsCount_AfterProcessing()
        {
            // Arrange: seed 3 hits (mix of hit/miss to cover both branches).
            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Hits[0] = new RaycastHit { HasHit = 1, RayId = PhysicsConstants.PackLosRayId(1, 2) };
            batch.Hits[1] = new RaycastHit { HasHit = 0 };
            batch.Hits[2] = new RaycastHit { HasHit = 1, RayId = PhysicsConstants.PackBulletRayId(7) };
            batch.Count   = 3;

            // Act
            _sys.Execute(_world, 0.016f);

            // Assert: count reset regardless of how many hits were processed.
            Assert.Equal(0, _world.GetSingleton<RaycastBatchData>().Count);
        }
    }
}
