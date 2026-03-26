using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace FDP.Toolkit.Physics.Tests
{
    /// <summary>
    /// Tests for <see cref="HitResolutionSystem"/> focused on the BS1-T010 requirement:
    /// bullet impacts must emit both a <see cref="HitEvent"/> and a
    /// <see cref="DetonationNotification"/>; LOS-check rays must emit neither.
    ///
    /// These tests use the overloaded constructor that accepts a <see cref="NetworkEntityMap"/>
    /// to opt in to <see cref="DetonationNotification"/> emission.
    /// Existing tests in <see cref="HitResolutionSystemTests"/> continue to use the
    /// default constructor (no map → no detonation notification).
    /// </summary>
    public class HitResolutionSystemDetonationTests : IDisposable
    {
        private readonly EntityRepository    _world;
        private readonly NetworkEntityMap    _entityMap;
        private readonly HitResolutionSystem _sys;

        public HitResolutionSystemDetonationTests()
        {
            _world = PhysicsTestWorldFactory.Create();

            // Register DetonationNotification for BS1-T010 paths.
            _world.RegisterEvent<DetonationNotification>();

            _entityMap = new NetworkEntityMap();

            _sys = new HitResolutionSystem(_entityMap);
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            PhysicsTestWorldFactory.DisposeBatch(_world);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity RegisterEntity(long netId)
        {
            var entity = _world.CreateEntity();
            _entityMap.Register(netId, entity);
            return entity;
        }

        // ── SC-1: Bullet hit → HitEvent AND DetonationNotification ───────────

        /// <summary>
        /// BS1-T010 SC-1: A bullet-ray hit must publish both <see cref="HitEvent"/> and
        /// <see cref="DetonationNotification"/> with the correct target network ID and
        /// world-space hit position.
        /// </summary>
        [Fact]
        public void BulletHit_EmitsBothHitEvent_AndDetonationNotification()
        {
            // Arrange
            const int bulletIdx = 7;
            const long hitNetId = 42L;
            const long shooterNetId = 10L;

            var hitEntity     = RegisterEntity(hitNetId);
            var shooterEntity = RegisterEntity(shooterNetId);

            // Build the ray: start at (0,0,0), end at (10,0,0), hit at T=0.5 → world pos (5,0,0).
            var rayStart = new Vector3(0f, 0f, 0f);
            var rayEnd   = new Vector3(10f, 0f, 0f);

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Requests[0] = new RaycastRequest
            {
                Start        = rayStart,
                End          = rayEnd,
                RayId        = PhysicsConstants.PackBulletRayId(bulletIdx),
                IgnoreEntity = shooterEntity,  // BallisticsSystem sets IgnoreEntity = bullet's Shooter
            };
            batch.Hits[0] = new RaycastHit
            {
                HasHit    = 1,
                RayId     = PhysicsConstants.PackBulletRayId(bulletIdx),
                HitEntity = hitEntity,
                T         = 0.5f,
            };
            batch.Count = 1;

            // Act
            _sys.Run();
            _world.Bus.SwapBuffers();

            // Assert — HitEvent still published
            var hitEvents = _world.Bus.Consume<HitEvent>();
            Assert.Equal(1, hitEvents.Length);
            Assert.Equal(hitEntity, hitEvents[0].HitEntity);

            // Assert — DetonationNotification also published
            var detonations = _world.Bus.Consume<DetonationNotification>();
            Assert.Equal(1, detonations.Length);

            var det = detonations[0];
            Assert.Equal(hitNetId,     det.HitEntityId);
            Assert.Equal(shooterNetId, det.ShooterEntityId);

            // Hit position = rayStart + 0.5f * (rayEnd - rayStart) = (5, 0, 0)
            Assert.Equal(5f, det.HitX, precision: 4);
            Assert.Equal(0f, det.HitY, precision: 4);
            Assert.Equal(0f, det.HitZ, precision: 4);
        }

        // ── SC-2: LOS hit → no DetonationNotification ────────────────────────

        /// <summary>
        /// BS1-T010 SC-2: A LOS-check ray (bit 63 == 0) must NOT produce a
        /// <see cref="DetonationNotification"/> even when a <see cref="NetworkEntityMap"/>
        /// is present.
        /// </summary>
        [Fact]
        public void LosHit_DoesNotEmitDetonationNotification()
        {
            var observer = _world.CreateEntity();
            var target   = _world.CreateEntity();

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Requests[0] = new RaycastRequest
            {
                Start    = Vector3.Zero,
                End      = new Vector3(10f, 0f, 0f),
                RayId    = PhysicsConstants.PackLosRayId(observer.Index, target.Index),
                Observer = observer,
                Target   = target,
            };
            batch.Hits[0] = new RaycastHit
            {
                HasHit   = 1,
                RayId    = PhysicsConstants.PackLosRayId(observer.Index, target.Index),
                Observer = observer,
                Target   = target,
                T        = 0.3f,
            };
            batch.Count = 1;

            _sys.Run();
            _world.Bus.SwapBuffers();

            var detonations = _world.Bus.Consume<DetonationNotification>();
            Assert.Equal(0, detonations.Length);
        }

        // ── SC-3: Unknown entity IDs → still publish with zero IDs ───────────

        /// <summary>
        /// When a bullet hits an entity not registered in the <see cref="NetworkEntityMap"/>,
        /// the <see cref="DetonationNotification"/> is still published with zeroed IDs
        /// rather than throwing.
        /// </summary>
        [Fact]
        public void BulletHit_WithUnknownEntities_StillPublishesDetonationWithZeroIds()
        {
            // Entities are NOT registered in the EntityMap.
            var hitEntity = _world.CreateEntity();

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Requests[0] = new RaycastRequest
            {
                Start    = Vector3.Zero,
                End      = new Vector3(5f, 0f, 0f),
                RayId    = PhysicsConstants.PackBulletRayId(hitEntity.Index),
                IgnoreEntity = default,   // shooter entity not known
            };
            batch.Hits[0] = new RaycastHit
            {
                HasHit    = 1,
                RayId     = PhysicsConstants.PackBulletRayId(hitEntity.Index),
                HitEntity = hitEntity,
                T         = 1f,
            };
            batch.Count = 1;

            var ex = Record.Exception(() =>
            {
                _sys.Run();
                _world.Bus.SwapBuffers();
            });
            Assert.Null(ex);

            var detonations = _world.Bus.Consume<DetonationNotification>();
            // Event is still published; IDs are 0 because the entity map lookup failed.
            Assert.Equal(1, detonations.Length);
            Assert.Equal(0L, detonations[0].HitEntityId);
        }
    }
}
