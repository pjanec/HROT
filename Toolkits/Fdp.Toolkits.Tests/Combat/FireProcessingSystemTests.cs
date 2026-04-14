using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="FireProcessingSystem"/> after BS1-T007 / PACK-P003 refactor.
    ///
    /// The system now consumes <see cref="WeaponFireIntent"/> carrying local ECS
    /// <see cref="Entity"/> handles (PACK-P003) instead of resolving via NetworkEntityMap.
    /// After spawning the bullet it publishes a <see cref="WeaponFireNotification"/>.
    /// </summary>
    public class FireProcessingSystemTests : IDisposable
    {
        private readonly EntityRepository    _world;
        private readonly FireProcessingSystem _sys;

        public FireProcessingSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<BallisticProjectile>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterEvent<WeaponFireIntent>();
            _world.RegisterEvent<WeaponFireNotification>();

            _sys = new FireProcessingSystem();
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity SpawnShooter(Vector3 position, float muzzleVelocity = 800f)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            _world.AddComponent(entity, new WeaponState  { MuzzleVelocity = muzzleVelocity, Ammo = 10 });
            return entity;
        }

        private Entity SpawnShooterNonAuthoritative(Vector3 position, float muzzleVelocity = 800f)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            _world.AddComponent(entity, new WeaponState  { MuzzleVelocity = muzzleVelocity, Ammo = 10 });
            // Remote owner: primary owner ID differs from the local node ID.
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            return entity;
        }

        private Entity SpawnTarget(Vector3 position)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            return entity;
        }

        private void PublishIntent(Entity shooter, Entity target, int weaponIndex = 0)
        {
            _world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = shooter,
                Target      = target,
                WeaponIndex = weaponIndex,
            });
            _world.Bus.SwapBuffers();
        }

        // ── T007 SC-1: WeaponFireIntent spawns bullet + fires notification ──────

        /// <summary>
        /// BS1-T007 SC-1: A <see cref="WeaponFireIntent"/> with both entities alive must
        /// produce exactly one bullet entity with <see cref="BallisticProjectile"/> and
        /// position at the shooter's origin, with the shooter field set correctly.
        /// </summary>
        [Fact]
        public void FireProcessing_SpawnsBulletEntity_WhenWeaponFireIntentReceived()
        {
            var shooterPos = new Vector3(10f, 20f, 0f);
            var shooter    = SpawnShooter(shooterPos);
            var target     = SpawnTarget(new Vector3(20f, 20f, 0f));

            PublishIntent(shooter, target);
            _sys.Run();

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            Entity bulletEntity = default;
            foreach (var e in q)
            {
                bulletCount++;
                bulletEntity = e;
            }

            Assert.Equal(1, bulletCount);

            var tf = _world.GetComponent<SimTransform>(bulletEntity);
            Assert.Equal(shooterPos, tf.Position);

            var proj = _world.GetComponent<BallisticProjectile>(bulletEntity);
            Assert.Equal(shooter, proj.Shooter);
        }

        /// <summary>
        /// After spawning the bullet a <see cref="WeaponFireNotification"/> with the correct
        /// shooter and target entity handles must appear on the event bus.
        /// </summary>
        [Fact]
        public void FireProcessing_PublishesWeaponFireNotification_AfterBulletSpawned()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _sys.Run();

            // Notifications are published to the write buffer; swap to expose them.
            _world.Bus.SwapBuffers();

            var notifications = _world.Bus.Consume<WeaponFireNotification>();
            Assert.Equal(1, notifications.Length);
            // PACK-P003: notification carries Entity handles, not network IDs.
            Assert.Equal(shooter, notifications[0].Shooter);
            Assert.Equal(target,  notifications[0].Target);
        }

        // ── T007 SC-3: Dead entity → skip gracefully ────────────────────────

        /// <summary>
        /// BS1-T007 SC-3: When the shooter entity is destroyed before the system runs
        /// the event must be skipped silently (no bullet, no exception).
        /// </summary>
        [Fact]
        public void FireProcessing_SkipsEvent_WhenShooterEntityDead()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            // Destroy the shooter before the system ticks.
            PublishIntent(shooter, target);
            _world.DestroyEntity(shooter);

            var ex = Record.Exception(() => _sys.Run());
            Assert.Null(ex);

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(0, bulletCount);
        }

        /// <summary>
        /// When the target entity is dead, the system also skips without spawning a bullet.
        /// </summary>
        [Fact]
        public void FireProcessing_SkipsEvent_WhenTargetEntityDead()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _world.DestroyEntity(target);
            _sys.Run();

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(0, bulletCount);
        }

        // ── Bullet velocity uses muzzle velocity from computed direction ───────

        /// <summary>
        /// Bullet <see cref="SimVelocity.Linear"/> equals <c>direction × MuzzleVelocity</c>
        /// where direction is computed from the shooter-to-target vector.
        /// </summary>
        [Fact]
        public void FireProcessing_SetsBulletVelocity_UsingMuzzleVelocityAndComputedDirection()
        {
            const float muzzleVelocity = 800f;
            var shooter = SpawnShooter(Vector3.Zero, muzzleVelocity);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
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

        // ── TD-5: Ordering proxy — bullet must exist when notification is consumed ──

        /// <summary>
        /// TD-5 ordering-constraint proxy for BS1-T007:
        /// The <see cref="WeaponFireNotification"/> must be published AFTER the bullet entity
        /// has been created.
        ///
        /// <para>
        /// <b>Behavioral proxy:</b> This test asserts that EVERY <see cref="WeaponFireNotification"/>
        /// consumed after the system runs has a corresponding live <see cref="BallisticProjectile"/>
        /// entity whose <c>Shooter</c> matches the <c>Shooter</c> in the notification.
        /// This would fail if:
        ///   (a) the notification is emitted but bullet creation is removed or skipped,
        ///   (b) the notification <c>Shooter</c> is wrong, or
        ///   (c) the bullet is created after an already-consumed notification.
        /// </para>
        /// </summary>
        [Fact]
        public void FireProcessing_BulletExistsWhenNotificationIsConsumed_OrderingProxy()
        {
            var shooterEntity = SpawnShooter(Vector3.Zero);
            var targetEntity  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooterEntity, targetEntity);
            _sys.Run();
            _world.Bus.SwapBuffers();

            var notifications = _world.Bus.Consume<WeaponFireNotification>();
            Assert.Equal(1, notifications.Length);

            // PACK-P003: notification carries Entity handles directly.
            Assert.Equal(shooterEntity, notifications[0].Shooter);

            var query = _world.Query().With<BallisticProjectile>().Build();
            bool bulletFound = false;
            foreach (var bulletEntity in query)
            {
                var proj = _world.GetComponent<BallisticProjectile>(bulletEntity);
                if (proj.Shooter == shooterEntity)
                {
                    bulletFound = true;
                    break;
                }
            }

            Assert.True(bulletFound,
                "No BallisticProjectile with the expected Shooter was found in the world when " +
                "WeaponFireNotification was consumed.  This indicates the bullet was either not " +
                "created or was created for the wrong shooter.");
        }

        // ── Physics collider ──────────────────────────────────────────────────

        /// <summary>
        /// Bullet <see cref="PhysicsCollider.CollisionLayer"/> and <see cref="PhysicsCollider.Radius"/>
        /// must match the combat constants.
        /// </summary>
        [Fact]
        public void FireProcessing_SetsPhysicsCollider_WithBulletLayer()
        {
            var shooter = SpawnShooter(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
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

        // ── TD-6: Authority gate ──────────────────────────────────────────────
        /// <c>HasAuthority == false</c> (remote node owns the entity), no bullet
        /// should be spawned and no <see cref="WeaponFireNotification"/> published.
        /// </summary>
        [Fact]
        public void FireProcessing_SkipsBullet_WhenShooterNotAuthoritative()
        {
            // Spawn shooter as non-authoritative (remote owner).
            var shooter = SpawnShooterNonAuthoritative(Vector3.Zero);
            var target  = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(shooter, target);
            _sys.Run();

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(0, bulletCount);

            _world.Bus.SwapBuffers();
            var notifications = _world.Bus.Consume<WeaponFireNotification>();
            Assert.Equal(0, notifications.Length);
        }

        /// <summary>
        /// TD-6 complementary: when the shooter entity has <see cref="NetworkAuthority"/>
        /// with <c>HasAuthority == true</c>, the bullet is spawned normally.
        /// </summary>
        [Fact]
        public void FireProcessing_SpawnsBullet_WhenShooterIsAuthoritative()
        {
            // Explicit authority: primary owner == local node.
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(entity, new WeaponState { MuzzleVelocity = 800f, Ammo = 10 });
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));

            var target = SpawnTarget(new Vector3(10f, 0f, 0f));

            PublishIntent(entity, target);
            _sys.Run();

            var q = _world.Query().With<BallisticProjectile>().Build();
            int bulletCount = 0;
            foreach (var _ in q) bulletCount++;
            Assert.Equal(1, bulletCount);
        }
    }
}
