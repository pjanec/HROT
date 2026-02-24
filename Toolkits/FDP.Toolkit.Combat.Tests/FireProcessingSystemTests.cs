using System;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Physics.Components;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="FireProcessingSystem"/> (BCS-P5-T4, first half).
    /// Each test seeds a <see cref="FireRequestEvent"/>, runs the system, and asserts
    /// the resulting bullet entity state.  No mocking — real <see cref="EntityRepository"/>.
    /// </summary>
    public class FireProcessingSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly FireProcessingSystem _sys;

        public FireProcessingSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<BallisticProjectile>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterEvent<FireRequestEvent>();
            _world.RegisterEvent<HitEvent>();

            _sys = new FireProcessingSystem();
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a shooter entity with a WeaponState and SimTransform.
        /// </summary>
        private Entity SpawnShooter(Vector3 position, float muzzleVelocity = 800f)
        {
            var shooter = _world.CreateEntity();
            _world.AddComponent(shooter, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            _world.AddComponent(shooter, new WeaponState { MuzzleVelocity = muzzleVelocity, Ammo = 10 });
            return shooter;
        }

        /// <summary>
        /// Publishes a <see cref="FireRequestEvent"/> and swaps the event bus so the system
        /// can consume it this frame.
        /// </summary>
        private void PublishFireRequest(Entity shooter, Vector3 origin, Vector3 direction, Entity target = default)
        {
            _world.Bus.Publish(new FireRequestEvent
            {
                Shooter   = shooter,
                Target    = target,
                Origin    = origin,
                Direction = direction,
            });
            _world.Bus.SwapBuffers();
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="FireRequestEvent"/> must cause the system to create exactly one bullet
        /// entity carrying <see cref="BallisticProjectile"/>.
        /// <para>
        /// Also verifies: <c>SimTransform.Position == evt.Origin</c> and
        /// <c>BallisticProjectile.Shooter == evt.Shooter</c>.
        /// </para>
        /// </summary>
        [Fact]
        public void FireProcessing_SpawnsBulletEntity_WhenFireRequestReceived()
        {
            var origin  = new Vector3(10f, 20f, 0f);
            var dir     = new Vector3(1f, 0f, 0f);
            var shooter = SpawnShooter(origin);
            var target  = _world.CreateEntity();

            PublishFireRequest(shooter, origin, dir, target);

            _sys.Run();

            // Exactly one bullet entity should exist.
            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            Entity bulletEntity = default;
            foreach (var e in q)
            {
                bulletCount++;
                bulletEntity = e;
            }

            Assert.Equal(1, bulletCount);

            // SimTransform.Position must match the fire origin.
            var tf = _world.GetComponent<SimTransform>(bulletEntity);
            Assert.Equal(origin, tf.Position);

            // Shooter field must be set.
            var proj = _world.GetComponent<BallisticProjectile>(bulletEntity);
            Assert.Equal(shooter, proj.Shooter);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Bullet <see cref="SimVelocity.Linear"/> must equal
        /// <c>evt.Direction * WeaponState.MuzzleVelocity</c>.
        /// </summary>
        [Fact]
        public void FireProcessing_SetsBulletVelocity_UsingMuzzleVelocityFromWeapon()
        {
            const float muzzleVelocity = 800f;
            var origin  = Vector3.Zero;
            var dir     = new Vector3(1f, 0f, 0f);  // unit vector east
            var shooter = SpawnShooter(origin, muzzleVelocity);

            PublishFireRequest(shooter, origin, dir);

            _sys.Run();

            var q = _world.Query().With<SimVelocity>().With<BallisticProjectile>().Build();
            foreach (var e in q)
            {
                var vel = _world.GetComponent<SimVelocity>(e);
                Assert.Equal(new Vector3(muzzleVelocity, 0f, 0f), vel.Linear);
                return;
            }

            Assert.Fail("No bullet entity found.");
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When the shooter entity has been destroyed before the system runs the event
        /// must be skipped — no bullet entity is created.
        /// </summary>
        [Fact]
        public void FireProcessing_SkipsEvent_WhenShooterEntityNotAlive()
        {
            var origin  = Vector3.Zero;
            var dir     = new Vector3(1f, 0f, 0f);
            var shooter = SpawnShooter(origin);

            // Publish the fire request then immediately destroy the shooter.
            _world.Bus.Publish(new FireRequestEvent
            {
                Shooter   = shooter,
                Origin    = origin,
                Direction = dir,
            });
            _world.Bus.SwapBuffers();

            _world.DestroyEntity(shooter);

            _sys.Run();

            // No BallisticProjectile component should exist anywhere.
            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(0, bulletCount);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Bullet <see cref="PhysicsCollider.CollisionLayer"/> and
        /// <see cref="PhysicsCollider.Radius"/> must match the combat constants.
        /// </summary>
        [Fact]
        public void FireProcessing_SetsPhysicsCollider_WithBulletLayer()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            PublishFireRequest(shooter, Vector3.Zero, new Vector3(1f, 0f, 0f));

            _sys.Run();

            var q = _world.Query().With<PhysicsCollider>().With<BallisticProjectile>().Build();
            foreach (var e in q)
            {
                var collider = _world.GetComponent<PhysicsCollider>(e);
                Assert.Equal(CombatConstants.BulletCollisionLayer, collider.CollisionLayer);
                Assert.Equal(CombatConstants.BulletColliderRadius, collider.Radius);
                return;
            }

            Assert.Fail("No bullet entity with PhysicsCollider found.");
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Integration check: shooter + fire event → system run → bullet entity has
        /// <see cref="PhysicsCollider"/>.  Confirms end-to-end component attachment.
        /// </summary>
        [Fact]
        public void FireProcessing_AddsPhysicsCollider_ToNewBullet()
        {
            var shooter = SpawnShooter(new Vector3(5f, 5f, 0f));
            PublishFireRequest(shooter, new Vector3(5f, 5f, 0f), Vector3.Normalize(new Vector3(1f, 1f, 0f)));

            _sys.Run();

            var q = _world.Query().With<BallisticProjectile>().Build();
            foreach (var e in q)
            {
                Assert.True(_world.HasComponent<PhysicsCollider>(e),
                    "Bullet entity is missing PhysicsCollider.");
                return;
            }

            Assert.Fail("No bullet entity found.");
        }
    }
}
