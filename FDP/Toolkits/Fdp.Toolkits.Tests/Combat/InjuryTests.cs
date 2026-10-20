using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2155</c> (R-263 D1 / R-2011, docs/DESIGN_Injury_States.md) — the four human states, derived from <see cref="Health"/> and
    /// the type's <c>Gen.Injury</c>: an incapacitated soldier stops moving and firing but stays alive, bleeds out on the Health owner only,
    /// is no threat and no target, and stays down when set down; a type without <c>Gen.Injury</c> behaves as before.
    /// </summary>
    public sealed class InjuryTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public InjuryTests()
        {
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<InjuryThresholds>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<MobilityKill>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<SimTransform>();
        }

        public void Dispose() => _world.Dispose();

        private const ActorCapabilities MoveAndFire = ActorCapabilities.CanMove | ActorCapabilities.CanShoot;

        /// <summary>A soldier of a type declaring <c>Gen.Injury</c> (or not), through the real translator.</summary>
        private Entity Soldier(float current, InjuryDefDto? injury = null, bool ownsHealth = true)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new Health { Current = current, Max = 100f });
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = MoveAndFire | ActorCapabilities.CanInteract });
            if (!ownsHealth) _world.AddComponent(e, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            var t = new TkbTemplate("Soldier", 4242L);
            if (injury != null) t.AddDescriptor(injury);
            new InjuryTkbTranslator().Inject(_world, e, t);
            return e;
        }

        private ActorCapabilities Caps(Entity e) => _world.GetComponentRO<ActorCapabilityState>(e).Capabilities;

        private void Hit(Entity e) => CombatLife.ApplyHitCapabilities(_world, e, in _world.GetComponentRO<Health>(e));

        /// <summary>⭐ I1 / I2 — the state is read off health and the type's band: ok, wounded below 60 %, incapacitated below 25 %, dead at 0.</summary>
        [Fact]
        public void TheState_IsReadOffHealth_AgainstTheTypesBand()
        {
            Assert.Equal(InjuryState.Ok, Injury.StateOf(_world, Soldier(80f, new InjuryDefDto())));
            Assert.Equal(InjuryState.Wounded, Injury.StateOf(_world, Soldier(40f, new InjuryDefDto())));
            Assert.Equal(InjuryState.Incapacitated, Injury.StateOf(_world, Soldier(20f, new InjuryDefDto())));
            Assert.Equal(InjuryState.Dead, Injury.StateOf(_world, Soldier(0f, new InjuryDefDto())));
        }

        /// <summary>⭐ I6 — a hit that leaves him below the band strips move AND fire, but he is alive (not a body) and out of the fight; a
        /// wounding hit strips nothing. ✅ Red-proof: ApplyHitCapabilities without the incapacitated arm ⇒ he keeps firing ⇒ red.</summary>
        [Fact]
        public void AHitBelowTheBand_StripsMoveAndFire_ButLeavesHimAlive()
        {
            var down = Soldier(20f, new InjuryDefDto());
            Hit(down);
            Assert.Equal((ActorCapabilities)0, Caps(down) & MoveAndFire);
            Assert.True(CombatLife.IsAlive(_world, down));
            Assert.False(CombatLife.IsDestroyed(_world, down));
            Assert.False(Injury.IsInTheFight(_world, down));
            Assert.True(Injury.IsIncapacitated(_world, down));

            var hurt = Soldier(40f, new InjuryDefDto());
            Hit(hurt);
            Assert.Equal(MoveAndFire, Caps(hurt) & MoveAndFire);
            Assert.True(Injury.IsInTheFight(_world, hurt));
        }

        /// <summary>⭐ I2 — a type without <c>Gen.Injury</c> is unchanged: ok at 10 HP, keeps moving and firing until dead.</summary>
        [Fact]
        public void ATypeWithoutGenInjury_IsOkUntilDead()
        {
            var old = Soldier(10f);
            Assert.False(_world.HasComponent<InjuryThresholds>(old));
            Hit(old);
            Assert.Equal(InjuryState.Ok, Injury.StateOf(_world, old));
            Assert.Equal(MoveAndFire, Caps(old) & MoveAndFire);
        }

        /// <summary>⭐ I3 — the Health owner bleeds an incapacitated soldier to death in <c>BleedOutSeconds</c>, in whole-second steps (one
        /// health change a second on the wire, not one a frame); a node that does not own his Health writes nothing, and a wounded one does
        /// not bleed. ✅ Red-proof: BleedOutSystem draining regardless of the state ⇒ the wounded soldier bleeds ⇒ red.</summary>
        [Fact]
        public void AnIncapacitatedSoldier_BleedsOut_OnTheHealthOwnerOnly()
        {
            var dto = new InjuryDefDto { IncapacitatedBelow = 0.25f, BleedOutSeconds = 100f };   // band pinned: 25 % to 0 in 100 s ⇒ 0.25 HP a second at Max 100
            var owned = Soldier(20f, dto);
            var replica = Soldier(20f, dto, ownsHealth: false);
            var wounded = Soldier(40f, dto);
            Hit(owned);
            var sys = new BleedOutSystem();

            sys.Execute(_world, 0.5f);
            Assert.Equal(20f, _world.GetComponentRO<Health>(owned).Current);   // half a step: nothing yet
            sys.Execute(_world, 0.5f);
            Assert.Equal(19.75f, _world.GetComponentRO<Health>(owned).Current, 3);

            for (int i = 0; i < 160; i++) sys.Execute(_world, 0.5f);   // 80 s more: 20 HP at 0.25/s
            Assert.Equal(0f, _world.GetComponentRO<Health>(owned).Current);
            Assert.Equal(InjuryState.Dead, Injury.StateOf(_world, owned));
            Assert.Equal((ActorCapabilities)0, Caps(owned) & MoveAndFire);

            Assert.Equal(20f, _world.GetComponentRO<Health>(replica).Current);
            Assert.Equal(40f, _world.GetComponentRO<Health>(wounded).Current);
        }

        /// <summary>⭐ I1 — every node reads the same state from the same replicated health: a second world with the same type and health
        /// agrees, with nothing sent but the health.</summary>
        [Fact]
        public void AReplica_ReadsTheSameState_FromTheReplicatedHealth()
        {
            using var other = new EntityRepository();
            other.RegisterComponent<Health>();
            other.RegisterComponent<InjuryThresholds>();
            var there = other.CreateEntity();
            other.AddComponent(there, new Health { Current = 20f, Max = 100f });
            var t = new TkbTemplate("Soldier", 4242L);
            t.AddDescriptor(new InjuryDefDto());
            new InjuryTkbTranslator().Inject(other, there, t);

            Assert.Equal(Injury.StateOf(_world, Soldier(20f, new InjuryDefDto())), Injury.StateOf(other, there));
        }

        /// <summary>⭐ I4 — a man down is no threat (danger 0) and no target (the EQS alive filter drops him); a wounded one is still both.
        /// ✅ Red-proof: IsOutOfTheFight back to "Health ≤ 0" only ⇒ danger stays 0.3 ⇒ red.</summary>
        [Fact]
        public void AnIncapacitatedContact_IsNoThreat_AndNoTarget()
        {
            var self = _world.CreateEntity();
            var down = Soldier(20f, new InjuryDefDto());
            var hurt = Soldier(40f, new InjuryDefDto());
            Assert.Equal(0f, ThreatDanger.Of(_world, self, down));
            Assert.True(ThreatDanger.IsOutOfTheFight(_world, down));
            Assert.Equal(ThreatDanger.Unarmed, ThreatDanger.Of(_world, self, hurt));

            var candidates = new EqsResult[] { new() { EntityId = (long)down.PackedValue }, new() { EntityId = (long)hurt.PackedValue } };
            var sensor = default(EqsSensor);
            new AliveFilterTest().ExecuteBatch(self, ref sensor, _world, candidates);
            Assert.Equal(-1L, candidates[0].EntityId);
            Assert.Equal((long)hurt.PackedValue, candidates[1].EntityId);
        }

        /// <summary>⭐⭐ <c>CE-2318</c> — a man DOWN lies: his logical stance is Prone whatever his brain last ordered, so the aim point, the
        /// sight points and the round's body band follow the lying body his hit boxes take — rounds no longer go over him at a standing
        /// man's 0.85 m. A wounded man on his feet keeps his stance. ✅ Red-proof: <c>LogicalStance.Of</c> without the down rule ⇒ the
        /// downed man aims at the standing height ⇒ red.</summary>
        [Fact]
        public void CE2318_AManDown_LiesProne_SoTheAimPointIsTheLyingBody()
        {
            _world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            Entity At(float health)
            {
                var e = Soldier(health, new InjuryDefDto());
                _world.AddComponent(e, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = StanceId.Standing });
                _world.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                return e;
            }
            var down = At(20f);
            var dead = At(0f);
            var hurt = At(40f);
            Assert.Equal(StanceId.Prone, Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(_world, down));
            Assert.Equal(StanceId.Prone, Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(_world, dead));
            Assert.Equal(StanceId.Standing, Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(_world, hurt));

            Assert.InRange(AimPoint.Middle(_world, down, out var stance, out _).Z, 0f, 0.3f);
            Assert.Equal(StanceId.Prone, stance);
            Assert.InRange(AimPoint.Middle(_world, hurt, out _, out _).Z, 0.7f, 1.0f);
        }

        /// <summary>⭐ I6 — a casualty set down stays down: leaving a carrier gives move and fire back only to someone in the fight.
        /// ✅ Red-proof: Leave restoring for any alive passenger ⇒ the casualty stands up armed ⇒ red.</summary>
        [Fact]
        public void ACasualtySetDown_StaysDown()
        {
            var vehicle = _world.CreateEntity();
            var casualty = Soldier(20f, new InjuryDefDto());
            Hit(casualty);
            _world.AddComponent(casualty, new IsEmbarkedTag { VehicleEntity = vehicle });
            Embarkation.Leave(_world, casualty, Vector3.Zero);
            Assert.Equal((ActorCapabilities)0, Caps(casualty) & MoveAndFire);

            var fit = Soldier(90f, new InjuryDefDto());
            _world.GetComponentRW<ActorCapabilityState>(fit).Capabilities &= ~MoveAndFire;   // as boarding leaves him
            _world.AddComponent(fit, new IsEmbarkedTag { VehicleEntity = vehicle });
            Embarkation.Leave(_world, fit, Vector3.Zero);
            Assert.Equal(MoveAndFire, Caps(fit) & MoveAndFire);
        }

        /// <summary>A band that cannot be read (incapacitated above wounded) is refused at spawn, not discovered in play.</summary>
        [Fact]
        public void AnUnreadableBand_IsRefusedAtSpawn()
            => Assert.Throws<InvalidOperationException>(() => Soldier(100f, new InjuryDefDto { WoundedBelow = 0.2f, IncapacitatedBelow = 0.5f }));

        /// <summary>⭐ CE-1082 (slice I-b) — the TKB's <c>WoundedSpeedFactor</c> (default 0.6) is stamped beside the band, and
        /// <c>Injury.SpeedFactor</c> applies it only while WOUNDED (ok, incapacitated and dead men are 1 — the down do not move at all).</summary>
        [Fact]
        public void CE1082_TheWoundedSpeedFactor_IsStamped_AndAppliesOnlyWhileWounded()
        {
            var thresholds = Fdp.Toolkit.Combat.Translators.InjuryTkbTranslator.Thresholds(new Fdp.Toolkit.Tkb.Domain.InjuryDefDto());
            Assert.Equal(0.6f, thresholds.WoundedSpeedFactor);
            using var w = new EntityRepository();
            w.RegisterComponent<Health>();
            w.RegisterComponent<InjuryThresholds>();
            float At(float health)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new Health { Current = health, Max = 100f });
                w.AddComponent(e, thresholds);
                return Injury.SpeedFactor(w, e);
            }
            Assert.Equal(1f, At(90f));     // ok
            Assert.Equal(0.6f, At(50f));   // wounded
            Assert.Equal(1f, At(20f));     // incapacitated: he does not move at all (CanMove is stripped)
        }
    }
}
