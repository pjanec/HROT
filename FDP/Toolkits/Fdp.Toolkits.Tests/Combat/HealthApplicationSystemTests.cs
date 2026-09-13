using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HealthApplicationSystem"/> (BS1-T014).
    /// </summary>
    public class HealthApplicationSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly HealthApplicationSystem _sys;

        public HealthApplicationSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterComponent<NetworkIdentity>();
            _world.RegisterEvent<DamageAssessedEvent>();
            _world.RegisterManagedEvent<DestroyEntityCommand>();   // CE-267

            _sys = new HealthApplicationSystem();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private Entity SpawnTarget(float currentHealth, float maxHealth = 100f,
            bool authoritative = true, bool addCapabilities = false)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = currentHealth, Max = maxHealth });
            _world.AddComponent(entity, new NetworkAuthority(
                primaryOwnerId: authoritative ? 1 : 2,
                localNodeId: 1));

            // CE-267 — the destroy path addresses the entity by NETWORK id, so every target needs one.
            _world.AddComponent(entity, new NetworkIdentity(9000 + entity.Index));

            if (addCapabilities)
                _world.AddComponent(entity, new ActorCapabilityState
                {
                    Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
                });

            return entity;
        }

        private void PublishEvent(Entity hitEntity, float totalDamage)
        {
            _world.Bus.Publish(new DamageAssessedEvent
            {
                HitEntity   = hitEntity,
                TotalDamage = totalDamage,
            });
            _world.Bus.SwapBuffers();
        }

        // â”€â”€ SC-1: Health decremented â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BS1-T014 SC-1: When an authoritative node receives a <see cref="DamageAssessedEvent"/>
        /// for a known entity, <see cref="Health.Current"/> must be reduced by the given amount.
        /// </summary>
        [Fact]
        public void HealthApplication_DecrementsHealth_WhenAuthoritative()
        {
            var entity = SpawnTarget(currentHealth: 100f);

            PublishEvent(hitEntity: entity, totalDamage: 30f);
            _sys.Execute(_world, 0.016f);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(70f, health.Current);
        }

        // â”€â”€ SC-2: Health cannot go below zero â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BS1-T014 SC-2: <see cref="Health.Current"/> must be clamped to 0 and never go negative.
        /// </summary>
        [Fact]
        public void HealthApplication_ClampsHealthToZero_WhenDamageExceedsCurrentHP()
        {
            var entity = SpawnTarget(currentHealth: 10f);

            PublishEvent(hitEntity: entity, totalDamage: 50f);
            _sys.Execute(_world, 0.016f);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(0f, health.Current);
        }

        // â”€â”€ SC-3: Zero HP strips capabilities â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BS1-T014 SC-3: When <see cref="Health.Current"/> reaches 0, both
        /// <see cref="ActorCapabilities.CanMove"/> and <see cref="ActorCapabilities.CanShoot"/>
        /// must be cleared from <see cref="ActorCapabilityState"/>.
        /// </summary>
        [Fact]
        public void HealthApplication_StripsCapabilities_WhenHealthReachesZero()
        {
            var entity = SpawnTarget(currentHealth: 10f, addCapabilities: true);

            PublishEvent(hitEntity: entity, totalDamage: 50f);
            _sys.Execute(_world, 0.016f);

            var caps = _world.GetComponent<ActorCapabilityState>(entity);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove));
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanShoot));
        }

        // â”€â”€ SC-4: Non-authority â†’ no health change â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BS1-T014 SC-4: A non-authoritative node must not alter the health of the entity.
        /// </summary>
        [Fact]
        public void HealthApplication_SkipsUpdate_WhenNotAuthoritative()
        {
            var entity = SpawnTarget(currentHealth: 100f, authoritative: false);

            PublishEvent(hitEntity: entity, totalDamage: 30f);
            _sys.Execute(_world, 0.016f);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(100f, health.Current);
        }

        // â”€â”€ PACK-M002: Non-lethal hit strips CanMove only â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// PACK-M002 SC-1: A non-lethal <see cref="DamageAssessedEvent"/> must reduce HP
        /// and strip <see cref="ActorCapabilities.CanMove"/> while preserving all other
        /// capabilities (e.g. <see cref="ActorCapabilities.CanInteract"/>).
        /// </summary>
        [Fact]
        public void HealthApplication_NonLethalHit_StripsCanMove_PreservesOtherCapabilities()
        {
            // Entity at max health with both CanMove and CanInteract.
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = 500f, Max = 500f });
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            _world.AddComponent(entity, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract,
            });

            // Non-lethal hit: 100 damage out of 500 max.
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = entity, TotalDamage = 100f });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(400f, health.Current);  // HP reduced

            var caps = _world.GetComponent<ActorCapabilityState>(entity);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove),
                "CanMove must be stripped on a non-lethal hit.");
            Assert.True(caps.Capabilities.HasFlag(ActorCapabilities.CanInteract),
                "CanInteract must NOT be stripped on a non-lethal hit.");
        }

        /// <summary>
        /// PACK-M002 SC-2 (regression guard): A lethal hit (HP â†’ 0) must not throw
        /// and must also strip CanMove (both CanMove and CanShoot are cleared at zero HP â€”
        /// the existing behaviour is preserved).
        /// </summary>
        [Fact]
        public void HealthApplication_LethalHit_DoesNotThrow_CanMoveAlsoStripped()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = 500f, Max = 500f });
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            _world.AddComponent(entity, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
            });

            // Lethal hit: exactly 500 damage.
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = entity, TotalDamage = 500f });
            _world.Bus.SwapBuffers();

            var ex = Record.Exception(() => _sys.Execute(_world, 0.016f));
            Assert.Null(ex);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(0f, health.Current);

            var caps = _world.GetComponent<ActorCapabilityState>(entity);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove));
        }

        /// <summary>
        /// PACK-M002 SC-1b: When the entity does NOT have an
        /// <see cref="ActorCapabilityState"/> component, a non-lethal hit must still
        /// reduce HP without throwing (skip-if-absent contract).
        /// </summary>
        [Fact]
        public void HealthApplication_NonLethalHit_NoCapabilityState_SkipsSilently()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = 200f, Max = 200f });
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            // No ActorCapabilityState registered.

            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = entity, TotalDamage = 50f });
            _world.Bus.SwapBuffers();

            var ex = Record.Exception(() => _sys.Execute(_world, 0.016f));
            Assert.Null(ex);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(150f, health.Current);
        }

        // ── CE-267: the KILL must DESTROY, and it must replicate ──────────────────────────────

        /// <summary>⭐ Drains whatever destroy commands the system published this frame.</summary>
        private System.Collections.Generic.List<DestroyEntityCommand> DrainDestroys()
        {
            _world.Bus.SwapBuffers();
            var got = new System.Collections.Generic.List<DestroyEntityCommand>();
            foreach (var c in _world.Bus.ReadManaged<DestroyEntityCommand>()) got.Add(c);
            return got;
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-267</c> — a lethal hit must publish <see cref="DestroyEntityCommand"/>.</b>
        ///
        /// <para>🔴 <b>The defect this pins, measured live on <c>hill-attack-close</c>.</b> This system used
        /// to clamp health to 0 and stop — its own comment said <i>"entity destruction is deferred to a
        /// separate workstream task"</i>. ⛔ <c>AimAndFireExecutor</c>'s ONLY success condition is
        /// <c>!world.IsAlive(target)</c>, so a target that was dead but not DESTROYED never ended the fire
        /// action: the behaviour tree could not leave its engage node and the attacking platoon cycled
        /// advance→fire→withdraw <b>forever</b>, draining ammo into a corpse *(42 → 19 over ten minutes)*.
        /// ⇒ the scenario had a terminal condition and could never reach it.</para>
        /// </summary>
        [Fact]
        public void ALethalHit_PublishesADestroyCommand()
        {
            var target = SpawnTarget(currentHealth: 10f, addCapabilities: true);
            PublishEvent(target, totalDamage: 25f);

            _sys.Execute(_world, 0.016f);

            var destroys = DrainDestroys();
            Assert.Single(destroys);
            Assert.Equal(_world.GetComponentRO<NetworkIdentity>(target).Value, destroys[0].NetworkId);
        }

        /// <summary>
        /// ⛔⛔ <b>…and a NON-lethal hit must publish NOTHING.</b> ⚠ Without this the rail above passes on a
        /// system that destroys on every hit — which would be a far worse defect than the one being fixed.
        /// </summary>
        [Fact]
        public void ANonLethalHit_PublishesNoDestroyCommand()
        {
            var target = SpawnTarget(currentHealth: 100f, addCapabilities: true);
            PublishEvent(target, totalDamage: 25f);

            _sys.Execute(_world, 0.016f);

            Assert.Empty(DrainDestroys());
            Assert.Equal(75f, _world.GetComponentRO<Health>(target).Current);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>EXACTLY ONCE — the command is published on the 0-HP TRANSITION, not on the STATE.</b>
        ///
        /// <para>🔴 This is not hypothetical: the shooters do not stop until the entity is gone, so further
        /// <c>DamageAssessedEvent</c>s against a corpse keep arriving while the two-ack teardown runs. ⛔ A
        /// test on <c>Current &lt;= 0</c> alone would re-publish a destroy for every one of them.</para>
        /// </summary>
        [Fact]
        public void RepeatedHitsOnACorpse_PublishTheDestroyExactlyOnce()
        {
            var target = SpawnTarget(currentHealth: 10f, addCapabilities: true);

            PublishEvent(target, totalDamage: 25f);
            _sys.Execute(_world, 0.016f);
            Assert.Single(DrainDestroys());

            for (int i = 0; i < 3; i++)
            {
                PublishEvent(target, totalDamage: 25f);
                _sys.Execute(_world, 0.016f);
                Assert.Empty(DrainDestroys());
            }
        }

        /// <summary>
        /// ⛔ <b>A NON-AUTHORITATIVE node must not destroy anything</b> — it does not own the entity, and two
        /// nodes publishing the same teardown is the duplicate-destroy shape. ⚠ The existing authority gate
        /// already covered the health write; this pins that it covers the destroy too.
        /// </summary>
        [Fact]
        public void ANonAuthoritativeNode_PublishesNoDestroyCommand()
        {
            var target = SpawnTarget(currentHealth: 10f, authoritative: false, addCapabilities: true);
            PublishEvent(target, totalDamage: 25f);

            _sys.Execute(_world, 0.016f);

            Assert.Empty(DrainDestroys());
            Assert.Equal(10f, _world.GetComponentRO<Health>(target).Current);
        }
    }
}
