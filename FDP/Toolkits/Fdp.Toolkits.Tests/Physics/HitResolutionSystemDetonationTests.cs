using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    /// <summary>
    /// Tests for <see cref="HitResolutionSystem"/> focused on the PACK-P003 requirement:
    /// bullet impacts must emit both a <see cref="HitEvent"/> and a
    /// <see cref="DetonationNotification"/> with local ECS <see cref="Entity"/> handles;
    /// LOS-check rays must emit neither.
    ///
    /// <b>PACK-P003:</b> <see cref="HitResolutionSystem"/> no longer requires
    /// <see cref="Fdp.Toolkit.Replication.Services.NetworkEntityMap"/> — it always emits
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
        }

        public void Dispose()
        {
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

            // Build the ray: start at (0,0,0), end at (10,0,0), hit at T=0.5 -> world pos (5,0,0).
            var rayStart = new Vector3(0f, 0f, 0f);
            var rayEnd   = new Vector3(10f, 0f, 0f);

            _world.Bus.Publish(new RaycastResultEvent
            {
                Hit = new RaycastHit
                {
                    HasHit       = 1,
                    RayId        = PhysicsConstants.PackBulletRayId(bulletIdx),
                    HitEntity    = hitEntity,
                    IgnoreEntity = shooterEntity,  // BallisticsSystem sets IgnoreEntity = bullet's Shooter
                    Start        = rayStart,
                    End          = rayEnd,
                    T            = 0.5f,
                }
            });
            _world.Bus.SwapBuffers();

            // Act
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            // Assert — HitEvent still published
            var hitEvents = _world.Bus.Read<HitEvent>();
            Assert.Equal(1, hitEvents.Length);
            Assert.Equal(hitEntity, hitEvents[0].HitEntity);

            // Assert — DetonationNotification also published with Entity handles
            var detonations = _world.Bus.Read<DetonationNotification>();
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

        // ── ⭐ Buildings §3d P2 (R-217): the walls crossed BEFORE the struck unit, on this segment ─────────────

        /// <summary>A wooden fence (0.05 m, 3 mm RHA) at x = 5 on the segment (0,0,1.6) → (10,0,1.6). A unit struck at x = 4 (before it)
        /// takes the round as it left the muzzle; one struck at x = 8 (beyond it) takes what got through: rifle 5 mm vs 3 mm passes
        /// with chance 1 (full damage) but arrives with 2 mm; a 2.6 mm round passes with chance 0.17 ⇒ 17 % of its damage.</summary>
        [Theory]
        [InlineData(0.4f, 5f,   5f,   25f)]     // before the fence
        [InlineData(0.8f, 5f,   2f,   25f)]     // beyond: chance 1, penetration − 3
        [InlineData(0.8f, 2.6f, 1e-3f, 4.1667f)]  // beyond: 2.6/3 = 0.867 ⇒ chance (0.867 − 0.8)/0.4 = 0.1667 ⇒ 25 × 0.1667; spent, not unknown
        public void R217_AStruckUnit_TakesTheRoundAsItArrived_FencesBeforeItCount_FencesBehindItDoNot(float t, float pen, float expectPen, float expectDamage)
        {
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
            _world.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "fence", "height": 2 }, "geometry": { "type": "LineString", "coordinates": [[5,-5],[5,5]] } } ] }
            """));
            var bullet = _world.CreateEntity();
            _world.AddComponent(bullet, new Fdp.Toolkit.Combat.Components.BallisticProjectile { Damage = 25f, Penetration = pen });
            var unit = _world.CreateEntity();
            _world.Bus.Publish(new RaycastResultEvent { Hit = new RaycastHit
            {
                HasHit = 1, RayId = PhysicsConstants.PackBulletRayId(bullet.Index), HitEntity = unit,
                Start = new Vector3(0, 0, 1.6f), End = new Vector3(10, 0, 1.6f), T = t,
            } });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();
            var det = Assert.Single(_world.Bus.Read<DetonationNotification>().ToArray());
            Assert.Equal(expectPen, det.Penetration, 3);
            Assert.Equal(expectDamage, det.Damage, 2);
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

            _world.Bus.Publish(new RaycastResultEvent
            {
                Hit = new RaycastHit
                {
                    HasHit   = 1,
                    RayId    = PhysicsConstants.PackLosRayId(observer.Index, target.Index),
                    Observer = observer,
                    Target   = target,
                    T        = 0.3f,
                }
            });
            _world.Bus.SwapBuffers();

            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            var detonations = _world.Bus.Read<DetonationNotification>();
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

            _world.Bus.Publish(new RaycastResultEvent
            {
                Hit = new RaycastHit
                {
                    HasHit       = 1,
                    RayId        = PhysicsConstants.PackBulletRayId(hitEntity.Index),
                    HitEntity    = hitEntity,
                    IgnoreEntity = shooterEntity,
                    Start        = Vector3.Zero,
                    End          = new Vector3(5f, 0f, 0f),
                    T            = 1f,
                }
            });
            _world.Bus.SwapBuffers();

            var ex = Record.Exception(() =>
            {
                _sys.Execute(_world, 0.016f);
                _world.Bus.SwapBuffers();
            });
            Assert.Null(ex);

            var detonations = _world.Bus.Read<DetonationNotification>();
            Assert.Equal(1, detonations.Length);
            // Entity handles carried directly — no network-ID lookup needed.
            Assert.Equal(hitEntity,     detonations[0].Target);
            Assert.Equal(shooterEntity, detonations[0].Shooter);
        }

        /// <summary>
        /// ⭐ <c>CE-3071</c> — the detonation carries the BULLET's munition (the fired mount's penetration and damage), so the
        /// damage step knows what struck. A bullet with no numbers reaches it as an unknown munition (0, 0 here — no bullet).
        /// </summary>
        [Fact]
        public void CE3071_ABulletHit_CarriesTheBulletsMunition_IntoTheDetonation()
        {
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
            var hitEntity = _world.CreateEntity();
            var shooter   = _world.CreateEntity();
            var bullet    = _world.CreateEntity();
            _world.AddComponent(bullet, new Fdp.Toolkit.Combat.Components.BallisticProjectile { Shooter = shooter, Damage = 1100, Penetration = 600 });

            _world.Bus.Publish(new RaycastResultEvent
            {
                Hit = new RaycastHit
                {
                    HasHit = 1, RayId = PhysicsConstants.PackBulletRayId(bullet.Index), HitEntity = hitEntity,
                    IgnoreEntity = shooter, Start = Vector3.Zero, End = new Vector3(10, 0, 0), T = 0.5f,
                }
            });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();

            var det = Assert.Single(_world.Bus.Read<DetonationNotification>().ToArray());
            Assert.Equal(600f,  det.Penetration);
            Assert.Equal(1100f, det.Damage);
        }
    }
}
