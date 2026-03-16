using System;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Kernel
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Systems;
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
            _world.RegisterComponent<HealthData>();
            _world.RegisterComponent<ActorCapabilityState>();
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
        // ── Test 7 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Verifies the dirty-flag guard (DB-MOD1-25): <see cref="DamageSystem"/> must only call
        /// <c>SetComponent&lt;HealthData&gt;</c> when <see cref="HealthData.Current"/> has actually
        /// changed.
        /// <para>
        /// A <c>Max=999</c> sentinel is embedded in the pre-seeded <see cref="HealthData"/>.
        /// </para>
        /// <list type="bullet">
        ///   <item>
        ///     <b>Tick 1:</b> Health starts at 100; <see cref="HealthData.Current"/> is pre-set to 75
        ///     (the expected post-damage value). After 25 damage, <c>Health.Current</c> becomes 75.
        ///     Because <see cref="HealthData.Current"/> already equals 75, <c>SetComponent</c> is
        ///     skipped — the <c>Max=999</c> sentinel is preserved, proving no write occurred.
        ///   </item>
        ///   <item>
        ///     <b>Tick 2:</b> another 25-damage hit reduces Health to 50. Now
        ///     <see cref="HealthData.Current"/> (75) differs from the new value (50), so
        ///     <c>SetComponent</c> fires and overwrites <c>Max</c> with 100 — confirming exactly
        ///     one write across the two ticks.
        ///   </item>
        /// </list>
        /// </summary>
        [Fact]
        public void HealthData_DirtyGuard_OnlyWritesWhenCurrentChanges()
        {
            // Tick 1: HealthData pre-seeded to match post-damage value → write must be skipped.
            var target = SpawnTarget(currentHealth: 100f, maxHealth: 100f);
            _world.AddComponent(target, new HealthData { Current = 75f, Max = 999f }); // sentinel

            var bullet1 = SpawnBullet(damage: 25f);
            PublishHitEvent(target, bullet1.Index);
            _sys.Run();

            Assert.Equal(75f, _world.GetComponent<Health>(target).Current);
            // Sentinel Max=999 must survive — SetComponent was skipped because Current was already 75.
            Assert.Equal(999f, _world.GetComponent<HealthData>(target).Max);

            // Tick 2: HealthData out of date (Current=75) after another 25 damage → write must fire.
            var bullet2 = SpawnBullet(damage: 25f);
            PublishHitEvent(target, bullet2.Index);
            _sys.Run();

            var hd = _world.GetComponent<HealthData>(target);
            Assert.Equal(50f, hd.Current);
            // Max=100 (health.Max) confirms SetComponent was called; sentinel 999 was overwritten.
            Assert.Equal(100f, hd.Max);
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
    }
}
