using System;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.Perception.Events;
using Xunit;

namespace FDP.Toolkit.Physics.Tests
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
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            PhysicsTestWorldFactory.DisposeBatch(_world);
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A hit with a LOS RayId (bit 63 = 0) must cause the system to publish a
        /// <see cref="TargetVisibleEvent"/> with the correct observer and target indices.
        /// </summary>
        [Fact]
        public void HitResolution_EmitsTargetVisibleEvent_ForLosHit()
        {
            // Arrange
            const int observerIdx = 10;
            const int targetIdx   = 20;

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Hits[0] = new RaycastHit
            {
                HasHit    = 1,
                RayId     = PhysicsConstants.PackLosRayId(observerIdx, targetIdx),
                HitEntity = default,
                T         = 0.5f,
            };
            batch.Count = 1;

            // Act
            _sys.Run();
            _world.Bus.SwapBuffers();

            // Assert
            var events = _world.Bus.Consume<TargetVisibleEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(observerIdx, events[0].ObserverEntityIndex);
            Assert.Equal(targetIdx,   events[0].TargetEntityIndex);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A hit with a bullet RayId (bit 63 = 1) must cause the system to publish a
        /// <see cref="HitEvent"/> with the correct <c>BulletIndex</c>.
        /// </summary>
        [Fact]
        public void HitResolution_EmitsHitEvent_ForBulletHit()
        {
            // Arrange
            const int bulletIdx = 42;

            var entity = _world.CreateEntity();

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Hits[0] = new RaycastHit
            {
                HasHit    = 1,
                RayId     = PhysicsConstants.PackBulletRayId(bulletIdx),
                HitEntity = entity,
                T         = 0.3f,
            };
            batch.Count = 1;

            // Act
            _sys.Run();
            _world.Bus.SwapBuffers();

            // Assert
            var events = _world.Bus.Consume<HitEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(bulletIdx, events[0].BulletIndex);
            Assert.Equal(entity,    events[0].HitEntity);
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
            _sys.Run();

            // Assert: count reset regardless of how many hits were processed.
            Assert.Equal(0, _world.GetSingleton<RaycastBatchData>().Count);
        }
    }
}
