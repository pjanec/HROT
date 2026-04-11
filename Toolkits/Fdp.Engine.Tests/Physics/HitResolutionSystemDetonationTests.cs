using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics.Systems;
using Xunit;

namespace FDP.Toolkit.Physics.Tests
{
    /// <summary>
    /// Tests for <see cref="HitResolutionSystem"/> focused on the PACK-P003 requirement:
    /// bullet impacts must emit both a <see cref="HitEvent"/> and a
    /// <see cref="DetonationNotification"/> with local ECS <see cref="Entity"/> handles;
    /// LOS-check rays must emit neither.
    ///
    /// <b>PACK-P003:</b> <see cref="HitResolutionSystem"/> no longer requires
    /// <see cref="FDP.Toolkit.Replication.Services.NetworkEntityMap"/> — it always emits
    /// <see cref="DetonationNotification"/> with local ECS handles.
    /// </summary>
    public class HitResolutionSystemDetonationTests : IDisposable
    {
        private readonly EntityRepository    _world;
        private readonly HitResolutionSystem _sys;

        public HitResolutionSystemDetonationTests()
        {
            _world = PhysicsTestWorldFactory.Create();

            // Register DetonationNotification for PACK-P003 paths.
            _world.RegisterEvent<DetonationNotification>();

            // PACK-P003: no-arg constructor — no NetworkEntityMap needed.
            _sys = new HitResolutionSystem();
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            PhysicsTestWorldFactory.DisposeBatch(_world);
        }

        // ── SC-1: Bullet hit → HitEvent AND DetonationNotification ───────────

        /// <summary>
        /// PACK-P003 SC-1: A bullet-ray hit must publish both <see cref="HitEvent"/> and
        /// <see cref="DetonationNotification"/> with the correct local ECS entity handles
        /// and world-space hit position.
        /// </summary>
        [Fact]
        public void BulletHit_EmitsBothHitEvent_AndDetonationNotification()
        {
            // Arrange
            const int bulletIdx = 7;

            var hitEntity     = _world.CreateEntity();
            var shooterEntity = _world.CreateEntity();

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

            // Assert — DetonationNotification also published with Entity handles
            var detonations = _world.Bus.Consume<DetonationNotification>();
            Assert.Equal(1, detonations.Length);

            var det = detonations[0];
            // PACK-P003: Entity handles, not network IDs.
            Assert.Equal(hitEntity,     det.Target);
            Assert.Equal(shooterEntity, det.Shooter);

            // Hit position = rayStart + 0.5f * (rayEnd - rayStart) = (5, 0, 0)
            Assert.Equal(5f, det.HitX, precision: 4);
            Assert.Equal(0f, det.HitY, precision: 4);
            Assert.Equal(0f, det.HitZ, precision: 4);
        }

        // ── SC-2: LOS hit → no DetonationNotification ────────────────────────

        /// <summary>
        /// PACK-P003 SC-2: A LOS-check ray (bit 63 == 0) must NOT produce a
        /// <see cref="DetonationNotification"/>.
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

        // ── SC-3: Always emits (no-arg constructor, offline scenario) ─────────

        /// <summary>
        /// PACK-P003 SC-3: <see cref="HitResolutionSystem"/> constructed with no
        /// arguments must always emit <see cref="DetonationNotification"/> for bullet
        /// impacts. In offline contexts the event goes unconsumed — this test verifies
        /// the event is published and carries the correct local Entity handles.
        /// </summary>
        [Fact]
        public void BulletHit_WithNoArgConstructor_AlwaysEmitsDetonationNotification()
        {
            var hitEntity     = _world.CreateEntity();
            var shooterEntity = _world.CreateEntity();

            ref var batch = ref _world.GetSingleton<RaycastBatchData>();
            batch.Requests[0] = new RaycastRequest
            {
                Start        = Vector3.Zero,
                End          = new Vector3(5f, 0f, 0f),
                RayId        = PhysicsConstants.PackBulletRayId(hitEntity.Index),
                IgnoreEntity = shooterEntity,
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
            Assert.Equal(1, detonations.Length);
            // Entity handles carried directly — no network-ID lookup needed.
            Assert.Equal(hitEntity,     detonations[0].Target);
            Assert.Equal(shooterEntity, detonations[0].Shooter);
        }
    }
}
