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
            _world.RegisterComponent<MobilityKill>();
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
            // ⭐ CE-523 (S8): the gate is the Health CLAIM — the owner holds it (the spawn sets it at birth; a grant moves it).
            if (authoritative) _world.SetAuthority<Health>(entity, true);

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
            _world.SetAuthority<Health>(entity, true);   // CE-523: the owner claims Health
            _world.AddComponent(entity, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract,
            });
            // CE-3092: the strip is now an opt-in of the platform (a vehicle's mobility kill).
            _world.AddComponent(entity, new MobilityKill { BelowFraction = 1f });

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
            _world.SetAuthority<Health>(entity, true);   // CE-523: the owner claims Health
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
            _world.SetAuthority<Health>(entity, true);   // CE-523: the owner claims Health
            // No ActorCapabilityState registered.

            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = entity, TotalDamage = 50f });
            _world.Bus.SwapBuffers();

            var ex = Record.Exception(() => _sys.Execute(_world, 0.016f));
            Assert.Null(ex);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(150f, health.Current);
        }

        // ── CE-523 (S8): the gate is the Health CLAIM, not the entity's primary owner ─────────────────────

        /// <summary>⭐ <c>CE-523</c> — SimHost created the entity (primary owner 2) and its Brain group, Health with it,
        /// was granted to this node: this node claims Health and applies the damage. The old entity-level gate dropped
        /// it (primary owner ≠ me) and no node applied it. 📄 <c>DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S8.</summary>
        [Fact]
        public void TheNodeThatClaimsHealth_AppliesTheDamage_EvenWhenAnotherNodeIsThePrimaryOwner()
        {
            var entity = SpawnTarget(currentHealth: 100f, authoritative: false);   // primary owner: node 2
            _world.SetAuthority<Health>(entity, true);                              // the Brain group was granted here

            PublishEvent(entity, 30f);
            _sys.Execute(_world, 0.016f);

            Assert.Equal(70f, _world.GetComponent<Health>(entity).Current);
        }

        /// <summary>⭐ <c>CE-523</c> — the converse: this node is the primary owner (it created the entity) but granted
        /// the Brain group away, so it no longer claims Health and must not apply the damage (the grantee does).</summary>
        [Fact]
        public void ThePrimaryOwnerThatGrantedHealthAway_DoesNotApplyTheDamage()
        {
            var entity = SpawnTarget(currentHealth: 100f, authoritative: true);
            _world.SetAuthority<Health>(entity, false);                             // the Brain group moved away

            PublishEvent(entity, 30f);
            _sys.Execute(_world, 0.016f);

            Assert.Equal(100f, _world.GetComponent<Health>(entity).Current);
        }

        /// <summary>A world with no network (no <see cref="NetworkAuthority"/>) owns everything: damage applies.</summary>
        [Fact]
        public void WithNoNetwork_TheDamageApplies()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = 100f, Max = 100f });

            PublishEvent(entity, 25f);
            _sys.Execute(_world, 0.016f);

            Assert.Equal(75f, _world.GetComponent<Health>(entity).Current);
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
        /// ⛔⛔ <b><c>CE-267</c> REVERTED — a lethal hit must NOT destroy the entity; the dead body stays.</b>
        ///
        /// <para>🔒 User ruling <c>2026-09-13</c>: <i>"dead entity should not vanish, it should stay dead in
        /// the world, every entity (no magic dead body vanishing)."</i> CE-267 had this system publish a
        /// <c>DestroyEntityCommand</c> at 0 HP so that downstream <c>!world.IsAlive(target)</c> checks came
        /// true — but <c>IsAlive</c> is ECS EXISTENCE, not combat-death; deleting the body to satisfy it was
        /// the wrong fix. ⭐ Combat-death is the STATE <c>Health.Current &lt;= 0</c> (+ capabilities
        /// stripped), which the target now carries while REMAINING alive as an ECS entity. This rail pins
        /// that: no destroy is published, the entity still exists, health is 0, and CanShoot is cleared.</para>
        /// </summary>
        [Fact]
        public void ALethalHit_DoesNotDestroyTheEntity_TheBodyStays()
        {
            var target = SpawnTarget(currentHealth: 10f, addCapabilities: true);
            PublishEvent(target, totalDamage: 25f);

            _sys.Execute(_world, 0.016f);

            Assert.Empty(DrainDestroys());                              // ⛔ no vanishing
            Assert.True(_world.IsAlive(target));                        // the body still exists
            Assert.Equal(0f, _world.GetComponentRO<Health>(target).Current);   // but combat-dead
            var caps = _world.GetComponentRO<ActorCapabilityState>(target).Capabilities;
            Assert.False(caps.HasFlag(ActorCapabilities.CanShoot));     // dead can't shoot
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
        /// ⛔ <b>Repeated hits on a corpse still never destroy it, and health stays clamped at 0.</b>
        /// Further <c>DamageAssessedEvent</c>s against a dead-but-present target keep arriving (shooters do
        /// not know it is dead until their own fire action reads <c>Health &lt;= 0</c>); none of them may
        /// remove the body.
        /// </summary>
        [Fact]
        public void RepeatedHitsOnACorpse_NeverDestroyIt()
        {
            var target = SpawnTarget(currentHealth: 10f, addCapabilities: true);

            for (int i = 0; i < 4; i++)
            {
                PublishEvent(target, totalDamage: 25f);
                _sys.Execute(_world, 0.016f);
                Assert.Empty(DrainDestroys());
                Assert.True(_world.IsAlive(target));
                Assert.Equal(0f, _world.GetComponentRO<Health>(target).Current);
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

        // ── CE-3092: the non-lethal strip is the platform's opt-in, not a rule for every unit ──

        private Entity SpawnCapable(float max, MobilityKill? kill)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new Health { Current = max, Max = max });
            _world.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            _world.SetAuthority<Health>(e, true);
            _world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
            });
            if (kill.HasValue) _world.AddComponent(e, kill.Value);
            return e;
        }

        private ActorCapabilities HitAndRead(Entity e, float damage)
        {
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = damage });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);
            return _world.GetComponent<ActorCapabilityState>(e).Capabilities;
        }

        /// <summary>
        /// CE-3092: a wounded soldier keeps moving. 📌 Measured: the unconditional strip froze a rifleman
        /// at the first graze, so every posture leg (take cover, flank, flee) stalled at the spot he was hit.
        /// </summary>
        [Fact]
        public void ANonLethalHit_OnAUnitWithoutAMobilityKill_KeepsCanMove_CE3092()
        {
            var e = SpawnCapable(100f, kill: null);
            var caps = HitAndRead(e, 75f);
            Assert.True(caps.HasFlag(ActorCapabilities.CanMove), "a wounded unit with no mobility-kill opt-in keeps moving");
            Assert.True(caps.HasFlag(ActorCapabilities.CanShoot));
        }

        /// <summary>CE-3092: death still stops every unit, opted in or not.</summary>
        [Fact]
        public void ALethalHit_OnAUnitWithoutAMobilityKill_StillStripsBoth_CE3092()
        {
            var e = SpawnCapable(100f, kill: null);
            var caps = HitAndRead(e, 100f);
            Assert.False(caps.HasFlag(ActorCapabilities.CanMove));
            Assert.False(caps.HasFlag(ActorCapabilities.CanShoot));
        }

        /// <summary>CE-3092: a threshold opt-in strips CanMove only once health drops BELOW the fraction.</summary>
        [Fact]
        public void AThresholdMobilityKill_StripsOnlyBelowTheFraction_CE3092()
        {
            var e = SpawnCapable(100f, new MobilityKill { BelowFraction = 0.5f });

            var afterGraze = HitAndRead(e, 30f);   // 70 % left
            Assert.True(afterGraze.HasFlag(ActorCapabilities.CanMove), "above the threshold the unit keeps moving");

            var afterHeavy = HitAndRead(e, 30f);   // 40 % left
            Assert.False(afterHeavy.HasFlag(ActorCapabilities.CanMove), "below the threshold the mobility kill applies");
            Assert.True(afterHeavy.HasFlag(ActorCapabilities.CanShoot), "a mobility kill never strips CanShoot");
        }
    }
}
