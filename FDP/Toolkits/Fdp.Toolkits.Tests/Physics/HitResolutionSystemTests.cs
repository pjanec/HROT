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
    /// Test pattern (event-based):
    ///   1. Publish <see cref="RaycastResultEvent"/> to the bus.
    ///   2. <c>world.Bus.SwapBuffers()</c> so the event is visible to the system.
    ///   3. <c>sys.Execute(world, dt)</c> — reads events and publishes domain events.
    ///   4. <c>world.Bus.SwapBuffers()</c> to expose events published by the system.
    ///   5. Assert consumed events.
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
            var observerEntity = new Entity(10, 1);
            var targetEntity   = new Entity(20, 1);

            _world.Bus.Publish(new RaycastResultEvent
            {
                Hit = new RaycastHit
                {
                    HasHit    = 1,
                    RayId     = PhysicsConstants.PackLosRayId(observerEntity.Index, targetEntity.Index),
                    Observer  = observerEntity,
                    Target    = targetEntity,
                    HitEntity = default,
                    T         = 0.5f,
                }
            });
            _world.Bus.SwapBuffers();

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
            var bulletEntity = _world.CreateEntity();
            var hitEntity    = _world.CreateEntity();

            _world.Bus.Publish(new RaycastResultEvent
            {
                Hit = new RaycastHit
                {
                    HasHit    = 1,
                    RayId     = PhysicsConstants.PackBulletRayId(bulletEntity.Index),
                    HitEntity = hitEntity,
                    T         = 0.3f,
                }
            });
            _world.Bus.SwapBuffers();

            // Act
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            // Assert
            var events = _world.Bus.Read<HitEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(bulletEntity, events[0].BulletEntity);
            Assert.Equal(hitEntity,    events[0].HitEntity);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ray with <c>HasHit == 0</c> must not emit any domain events.
        /// </summary>
        [Fact]
        public void HitResolution_SkipsMissedRays()
        {
            // Arrange: publish a miss.
            _world.Bus.Publish(new RaycastResultEvent
            {
                Hit = new RaycastHit { HasHit = 0, RayId = PhysicsConstants.PackLosRayId(1, 2) }
            });
            _world.Bus.SwapBuffers();

            // Act
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            // Assert: no TargetVisibleEvent or HitEvent emitted.
            Assert.Equal(0, _world.Bus.Read<TargetVisibleEvent>().Length);
            Assert.Equal(0, _world.Bus.Read<HitEvent>().Length);
        }
    }
}
