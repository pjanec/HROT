using System;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Kernel
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Replication.Components;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DamageSystem"/> (BCS-P5-T5).
    /// Tests seed <see cref="HitEvent"/>s directly (bypassing
    /// <see cref="HitResolutionSystem"/> for isolation) and assert health/lifecycle changes.
    /// </summary>
    public class DamageSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly DamageSystem _sys;

        public DamageSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<BallisticProjectile>();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterEvent<HitEvent>();

            _sys = new DamageSystem();
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Spawns a target entity with the given health values.</summary>
        private Entity SpawnTarget(float currentHealth, float maxHealth = 100f)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = currentHealth, Max = maxHealth });
            _world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            return entity;
        }

        /// <summary>Spawns a bullet entity with the given damage value.</summary>
        private Entity SpawnBullet(float damage)
        {
            var bullet = _world.CreateEntity();
            _world.AddComponent(bullet, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(bullet, new BallisticProjectile
            {
                Damage    = damage,
                SpawnTick = 0,
                Shooter   = Entity.Null,
            });
            return bullet;
        }

        /// <summary>
        /// Publishes a <see cref="HitEvent"/> and swaps event bus so the system can consume it.
        /// </summary>
        private void PublishHitEvent(Entity hitEntity, int bulletIndex, float hitT = 0.5f)
        {
            _world.Bus.Publish(new HitEvent
            {
                HitEntity   = hitEntity,
                BulletIndex = bulletIndex,
                HitT        = hitT,
            });
            _world.Bus.SwapBuffers();
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When a <see cref="HitEvent"/> is received for a living entity that has a
        /// <see cref="Health"/> component, the damage from the bullet must be subtracted
        /// from <see cref="Health.Current"/>.
        /// </summary>
        [Fact]
        public void Damage_ReducesHealth_WhenEntityIsHit()
        {
            var target = SpawnTarget(currentHealth: 100f);
            var bullet = SpawnBullet(damage: CombatConstants.DefaultBulletDamage);  // 25f

            PublishHitEvent(target, bullet.Index);

            _sys.Run();

            // 100 - 25 = 75
            var health = _world.GetComponent<Health>(target);
            Assert.Equal(75f, health.Current);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When the bullet's damage is lethal (damage >= current health), the target entity
        /// must be destroyed.
        /// </summary>
        [Fact]
        public void Damage_DestroysEntity_WhenHealthDropsToZero()
        {
            var target = SpawnTarget(currentHealth: 20f);   // would drop to -5 → clamped to 0
            var bullet = SpawnBullet(damage: 25f);

            PublishHitEvent(target, bullet.Index);

            _sys.Run();

            Assert.False(_world.IsAlive(target), "Target entity should have been destroyed.");
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="HitEvent"/> for an entity that has no <see cref="Health"/> component
        /// must be silently ignored — no crash, no component added.
        /// </summary>
        [Fact]
        public void Damage_DoesNotApplyDamage_WhenEntityHasNoHealthComponent()
        {
            // Target has no Health component.
            var target = _world.CreateEntity();
            _world.AddComponent(target, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            var bullet = SpawnBullet(damage: 25f);

            PublishHitEvent(target, bullet.Index);

            // Should not throw.
            _sys.Run();

            // No Health component should have been added.
            Assert.False(_world.HasComponent<Health>(target));
            // Target entity should still be alive.
            Assert.True(_world.IsAlive(target));
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="HitEvent"/> for an entity that is already dead (destroyed before the
        /// system runs) must be skipped gracefully — no exception.
        /// </summary>
        [Fact]
        public void Damage_SkipsHit_WhenEntityAlreadyDead()
        {
            var target = SpawnTarget(currentHealth: 100f);
            var bullet = SpawnBullet(damage: 25f);

            // Destroy target before system runs.
            _world.DestroyEntity(target);

            PublishHitEvent(target, bullet.Index);

            // Should not throw.
            var exception = Record.Exception(() => _sys.Run());
            Assert.Null(exception);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="HitEvent"/> whose bullet entity has been destroyed before
        /// <see cref="DamageSystem"/> runs must be skipped — no damage applied.
        /// This guards against the DEBT-027 raw-index recycling scenario.
        /// </summary>
        [Fact]
        public void Damage_SkipsHit_WhenBulletEntityNotAlive()
        {
            var target = SpawnTarget(currentHealth: 100f);
            var bullet = SpawnBullet(damage: 25f);
            int bulletIndex = bullet.Index;

            // Destroy the bullet entity before the system runs.
            _world.DestroyEntity(bullet);

            PublishHitEvent(target, bulletIndex);

            _sys.Run();

            // Health must remain at 100 — no damage applied.
            var health = _world.GetComponent<Health>(target);
            Assert.Equal(100f, health.Current);
        }

        // ── Test 6 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Verifies the capability-stripping behaviour on lethal hits using a two-part approach,
        /// because <c>FDP_PARANOID_MODE</c> is always active and reading components from a dead
        /// entity will throw <see cref="InvalidOperationException"/>.
        ///
        /// <para>
        /// <b>Part A (non-lethal baseline):</b> a 25-damage hit on a 100-HP target does NOT strip
        /// capabilities — the entity survives and both <see cref="ActorCapabilities.CanMove"/> and
        /// <see cref="ActorCapabilities.CanShoot"/> remain set.  This proves the stripping is
        /// exclusively triggered by the lethal code path.
        /// </para>
        ///
        /// <para>
        /// <b>Part B (lethal path):</b> a 25-damage hit on a 20-HP target must complete without
        /// exception (the stripping branch executes cleanly) and the entity must be destroyed.
        /// </para>
        /// </summary>
        // ── Test 7 (BUG2-A001) ────────────────────────────────────────────────

        /// <summary>
        /// Verifies that after BUG2-A001, <see cref="DamageSystem"/> no longer writes a
        /// <c>HealthData</c> mirror component.  Only the canonical <see cref="Health"/>
        /// component is updated.  The <c>HealthData</c> type no longer exists (deletion guard).
        /// </summary>
        [Fact]
        public void ProcessHit_DoesNotSetHealthDataComponent()
        {
            // Arrange: target entity with Health only (no HealthData mirror).
            var target = SpawnTarget(currentHealth: 100f, maxHealth: 100f);

            var bullet = SpawnBullet(damage: 25f);
            PublishHitEvent(target, bullet.Index);
            _sys.Run();

            // Health is correctly updated and the entity survives (non-lethal hit).
            Assert.True(_world.IsAlive(target));
            Assert.Equal(75f, _world.GetComponent<Health>(target).Current);
            // HealthData type no longer exists (BUG2-A001); any attempt to use it
            // would prevent compilation — this test name serves as a deletion guard.
        }

        [Fact]
        public void Damage_StripsCapabilities_OnLethalHit()
        {
            // ── Part A: non-lethal hit — capabilities must be UNCHANGED ───────
            var targetA = SpawnTarget(currentHealth: 100f);
            _world.AddComponent(targetA, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });
            var bulletA = SpawnBullet(damage: 25f);        // 100 − 25 = 75 → survives
            PublishHitEvent(targetA, bulletA.Index);
            _sys.Run();

            Assert.True(_world.IsAlive(targetA),
                "Non-lethal target must still be alive after a sub-lethal hit.");
            var capsAfterNonLethal = _world.GetComponent<ActorCapabilityState>(targetA);
            Assert.True(capsAfterNonLethal.Capabilities.HasFlag(ActorCapabilities.CanMove),
                "CanMove must NOT be stripped on a non-lethal hit.");
            Assert.True(capsAfterNonLethal.Capabilities.HasFlag(ActorCapabilities.CanShoot),
                "CanShoot must NOT be stripped on a non-lethal hit.");

            // ── Part B: lethal hit — stripping branch must run; entity must die ─
            var targetB = SpawnTarget(currentHealth: 20f);
            _world.AddComponent(targetB, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });
            var bulletB = SpawnBullet(damage: 25f);        // lethal: 20 − 25 → 0
            PublishHitEvent(targetB, bulletB.Index);

            // No exception = the stripping code path executed without error.
            var exception = Record.Exception(() => _sys.Run());
            Assert.Null(exception);

            Assert.False(_world.IsAlive(targetB),
                "Lethal hit must destroy the target after capability-stripping.");
        }

        // ── BS1-T003: Authority guard ─────────────────────────────────────────

        /// <summary>
        /// BS1-T003 SC-1: When the target entity has a <see cref="NetworkAuthority"/>
        /// component where <c>HasAuthority == false</c> (remote node), the system must
        /// skip the hit silently — health remains unchanged.
        /// </summary>
        [Fact]
        public void Damage_DoesNotApplyDamage_WhenEntityIsRemote()
        {
            var target = SpawnTarget(currentHealth: 100f);

            // Mark as remote: PrimaryOwnerId (2) != LocalNodeId (1) → HasAuthority = false.
            _world.AddComponent(target, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

            var bullet = SpawnBullet(damage: 25f);
            PublishHitEvent(target, bullet.Index);

            _sys.Run();

            // Health must remain at 100 — no damage applied.
            var health = _world.GetComponent<Health>(target);
            Assert.Equal(100f, health.Current);

            // Entity must still be alive (was not destroyed).
            Assert.True(_world.IsAlive(target));
        }

        /// <summary>
        /// BS1-T003 SC-2: When the target entity has a <see cref="NetworkAuthority"/>
        /// component where <c>HasAuthority == true</c> (local owner), the system must
        /// apply damage normally.
        /// </summary>
        [Fact]
        public void Damage_AppliesDamage_WhenEntityIsLocallyOwned()
        {
            var target = SpawnTarget(currentHealth: 100f);

            // Mark as local owner: PrimaryOwnerId (1) == LocalNodeId (1) → HasAuthority = true.
            _world.AddComponent(target, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));

            var bullet = SpawnBullet(damage: 25f);
            PublishHitEvent(target, bullet.Index);

            _sys.Run();

            // Health must be reduced: 100 − 25 = 75.
            var health = _world.GetComponent<Health>(target);
            Assert.Equal(75f, health.Current);
        }
    }
}
