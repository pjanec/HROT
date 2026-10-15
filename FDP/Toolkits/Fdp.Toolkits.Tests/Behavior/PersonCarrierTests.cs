using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2152</c> C-a (R-274 C / R-2009 C3 / R-2011 C5–C10, docs/DESIGN_Entity_Interactions.md §7.4.8) — a soldier carries or drags
    /// a wounded comrade with the SAME embark mechanism: only a man down may be carried, by a carrier in the fight; the mode rides in the
    /// list; the carrier cannot fire while carrying and gets his fire back when he puts him down; the casualty is held behind him by mode;
    /// a carrier who goes down drops the casualty alive.
    /// </summary>
    public sealed class PersonCarrierTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public PersonCarrierTests()
        {
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<CarrierSeats>();
            _world.RegisterComponent<OpenPassengerCabin>();
            _world.RegisterComponent<PersonCarrier>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<InjuryThresholds>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterEvent<DamageAssessedEvent>();
            _world.RegisterComponent<IsAssistingTag>();                 // ⭐ T-a
            _world.RegisterComponent<WeaponSystemParams>();             // ⭐ T-a — a two-man weapon
            _world.RegisterComponent<CarrierStations>();                // ⭐ T-a — the soldier's carry station (WS-2)
            if (!_world.IsComponentTypeRegistered<EntityInfo>()) _world.RegisterComponent<EntityInfo>();
        }

        public void Dispose() => _world.Dispose();

        private const ActorCapabilities MoveAndFire = ActorCapabilities.CanMove | ActorCapabilities.CanShoot;

        /// <summary>A soldier of a type declaring Gen.Carrier.PersonCarrier() and Gen.Injury, through the real translators.</summary>
        private Entity Soldier(float health, Vector3 at = default)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new Health { Current = health, Max = 100f });
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = MoveAndFire | ActorCapabilities.CanInteract });
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            var t = new TkbTemplate("Soldier", 4343L);
            t.AddDescriptor(CarrierDefDto.PersonCarrier());
            t.AddDescriptor(new InjuryDefDto());
            new CarrierTkbTranslator().Inject(_world, e, t);
            new InjuryTkbTranslator().Inject(_world, e, t);
            CombatLife.ApplyHitCapabilities(_world, e, in _world.GetComponentRO<Health>(e));
            return e;
        }

        private ActorCapabilities Caps(Entity e) => _world.GetComponentRO<ActorCapabilityState>(e).Capabilities;

        /// <summary>⭐ R-2009 C3 — a healthy soldier cannot be picked up, nor can a downed carrier carry; a man down can be, by a carrier in the
        /// fight, in the mode asked (a seated request is carried). ✅ Red-proof: TryEmbark without the person gate ⇒ the healthy one is
        /// carried ⇒ red.</summary>
        [Fact]
        public void OnlyAManDown_IsCarried_AndOnlyByACarrierInTheFight()
        {
            var carrier = Soldier(100f);
            var healthy = Soldier(90f);
            var down = Soldier(20f);
            var downCarrier = Soldier(10f);

            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, healthy, carrier));
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, down, downCarrier, mode: CarryMode.Dragged));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, down, carrier, mode: CarryMode.Dragged));
            Assert.Equal(CarryMode.Dragged, _world.GetComponentRO<IsEmbarkedTag>(down).Mode);
            Assert.Equal(CarryMode.Dragged, _world.GetComponentRO<PassengerBuffer>(carrier).ModeOf(0));

            var other = Soldier(100f);
            var down2 = Soldier(15f);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, down2, other));   // seated request ⇒ carried
            Assert.Equal(CarryMode.Carried, _world.GetComponentRO<IsEmbarkedTag>(down2).Mode);
        }

        /// <summary>⭐ C7 — the carrier keeps moving but cannot fire while carrying; setting the casualty down gives his fire back, and the
        /// casualty stays down. ✅ Red-proof: ReleaseCarrier doing nothing ⇒ he never fires again ⇒ red.</summary>
        [Fact]
        public void Carrying_TakesTheCarriersFire_PuttingHimDownGivesItBack()
        {
            var carrier = Soldier(100f);
            var down = Soldier(20f);
            Embarkation.TryEmbark(_world, down, carrier, mode: CarryMode.Carried);
            Assert.Equal(ActorCapabilities.CanMove, Caps(carrier) & MoveAndFire);

            Assert.True(Embarkation.Disembark(_world, down, carrier));
            Assert.Equal(MoveAndFire, Caps(carrier) & MoveAndFire);
            Assert.Equal((ActorCapabilities)0, Caps(down) & MoveAndFire);
            Assert.Equal(CarryMode.Seated, _world.GetComponentRO<PassengerBuffer>(carrier).ModeOf(0));
        }

        /// <summary>⭐ C10 — the casualty is held behind the carrier by mode (dragged ≈ 1 m behind his heading, carried on the shoulder),
        /// on every node that owns his position. ✅ Red-proof: CarryAndKill ignoring the offset ⇒ the dragged man sits on the carrier ⇒ red.</summary>
        [Fact]
        public void TheCasualty_IsHeldBehindTheCarrier_ByMode()
        {
            var dragger = Soldier(100f, new Vector3(10f, 0f, 0f));
            var dragged = Soldier(20f);
            var lifter = Soldier(100f, new Vector3(0f, 10f, 0f));
            var lifted = Soldier(20f);
            Embarkation.TryEmbark(_world, dragged, dragger, mode: CarryMode.Dragged);
            Embarkation.TryEmbark(_world, lifted, lifter, mode: CarryMode.Carried);

            new EmbarkedStateSystem().Execute(_world, 0.016f);

            var d = _world.GetComponentRO<SimTransform>(dragged).Position;
            Assert.Equal(9f, d.X, 3);   // heading +X ⇒ one metre behind on X
            Assert.Equal(0f, d.Y, 3);
            Assert.Equal(new Vector3(0f, 10f, 0f), _world.GetComponentRO<SimTransform>(lifted).Position);
        }

        /// <summary>⭐ C9 — a carrier who goes down (incapacitated, or killed) DROPS the casualty where he lies, alive — never killed with
        /// him. ✅ Red-proof: CarryAndKill without the drop ⇒ the casualty stays aboard a body ⇒ red.</summary>
        [Fact]
        public void ACarrierGoingDown_DropsTheCasualty_Alive()
        {
            var carrier = Soldier(100f);
            var down = Soldier(20f);
            Embarkation.TryEmbark(_world, down, carrier, mode: CarryMode.Carried);

            _world.GetComponentRW<Health>(carrier).Current = 0f;   // killed
            var sys = new EmbarkedStateSystem();
            sys.Execute(_world, 0.016f);

            Assert.False(_world.HasComponent<IsEmbarkedTag>(down));
            Assert.Equal(0, _world.GetComponentRO<PassengerBuffer>(carrier).Count);
            Assert.Equal(1, sys.Dropped);
            Assert.Equal(0, sys.Killed);
            Assert.True(CombatLife.IsAlive(_world, down));
        }

        /// <summary>⭐ C1 — the profile is a type fact: a person type gets <see cref="PersonCarrier"/> and survivable passengers; a vehicle type
        /// neither (its passengers still die with it).</summary>
        [Fact]
        public void TheProfile_IsStampedFromTheType()
        {
            var soldier = Soldier(100f);
            Assert.True(_world.HasComponent<PersonCarrier>(soldier));
            Assert.Equal(0.3f, _world.GetComponentRO<PersonCarrier>(soldier).DragSpeedFactor);
            Assert.Equal((byte)1, _world.GetComponentRO<CarrierSeats>(soldier).PassengersSurvive);
            Assert.Equal(2, _world.GetComponentRO<CarrierSeats>(soldier).SeatCount);   // ⭐ WS-2 (W-G) — the casualty seat + the carry station
            Assert.Equal(1, Embarkation.CapacityOf(_world, soldier));   // ...of which one carries a man

            var apc = _world.CreateEntity();
            var t = new TkbTemplate("Apc", 4344L);
            t.AddDescriptor(CarrierDefDto.PassengerSeats(4));
            new CarrierTkbTranslator().Inject(_world, apc, t);
            Assert.False(_world.HasComponent<PersonCarrier>(apc));
            Assert.Equal((byte)0, _world.GetComponentRO<CarrierSeats>(apc).PassengersSurvive);
        }
            // ── ⭐⭐ Team carry T-a (R-2017; docs/DESIGN_Entity_Interactions.md §7.5; STEER_Behaviors_To_Backend_Team_Carry.md) ──────────────────

        /// <summary>A weapon system of the given portability, carried on a soldier's carry station.</summary>
        private Entity Weapon(WeaponPortability portable)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Rotation = Quaternion.Identity });
            var t = new TkbTemplate("Weapon", 4345L);
            t.AddDescriptor(new WeaponSystemDto { MountKinds = MountKinds.Ground | MountKinds.Carried, Portable = portable });
            new WeaponSystemTkbTranslator().Inject(_world, e, t);
            return e;
        }

        /// <summary>
        /// ⭐⭐ T4 — a STRETCHER and a TWO-MAN weapon need a named second man — alive and in the fight, carrying nothing, the carrier's side,
        /// within 2 m; a one-man load refuses one. The pair's fire goes (T5), and the second man is tagged and stands still (T1, T3).
        /// ✅ Red-proof: TryEmbark without the team gate ⇒ a stretcher goes with nobody at the rear ⇒ red.
        /// </summary>
        [Fact]
        public void TA_AStretcher_AndATwoManWeapon_NeedAFitSecondMan_AOneManLoadRefusesOne()
        {
            var carrier = Soldier(100f);
            var casualty = Soldier(20f);
            var far = Soldier(100f, new Vector3(5f, 0f, 0f));
            var hurt = Soldier(10f, new Vector3(1f, 0f, 0f));
            var enemy = Soldier(100f, new Vector3(1f, 0f, 0f));
            _world.AddComponent(carrier, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(enemy, new EntityInfo { ForceId = ForceId.Hostile });
            var helper = Soldier(100f, new Vector3(1.5f, 0f, 0f));

            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, casualty, carrier, mode: CarryMode.Stretcher));                     // nobody
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, casualty, carrier, mode: CarryMode.Stretcher, assistant: far));     // 5 m
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, casualty, carrier, mode: CarryMode.Stretcher, assistant: hurt));    // down
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, casualty, carrier, mode: CarryMode.Stretcher, assistant: enemy));   // their side
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, casualty, carrier, mode: CarryMode.Carried, assistant: helper));    // one-man
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, casualty, carrier, mode: CarryMode.Stretcher, assistant: helper));

            Assert.Equal(helper, _world.GetComponentRO<PassengerBuffer>(carrier).Assistant);
            Assert.Equal(CarryMode.Stretcher, _world.GetComponentRO<IsEmbarkedTag>(casualty).Mode);
            Assert.Equal(carrier, _world.GetComponentRO<IsAssistingTag>(helper).Carrier);
            Assert.Equal(ActorCapabilities.CanMove, Caps(carrier) & MoveAndFire);   // hands full, still walks
            Assert.Equal((ActorCapabilities)0, Caps(helper) & MoveAndFire);         // hands full, placed at the handle
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, Soldier(20f), Soldier(100f, new Vector3(1f, 0f, 0f)),
                mode: CarryMode.Stretcher, assistant: helper));                                                                                // already assisting

            var gunner = Soldier(100f, new Vector3(0f, 20f, 0f));
            var loader = Soldier(100f, new Vector3(1f, 20f, 0f));
            var mortar = Weapon(WeaponPortability.TwoMan);
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, mortar, gunner));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, mortar, gunner, assistant: loader));
            Assert.Equal(ActorCapabilities.CanMove, Caps(gunner) & MoveAndFire);   // ⭐ a team weapon fills his hands (a one-man one does not)
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, Weapon(WeaponPortability.OneMan), Soldier(100f, new Vector3(0f, 40f, 0f)),
                assistant: Soldier(100f, new Vector3(1f, 40f, 0f))));
        }

        /// <summary>
        /// ⭐ T3 — the second man is PLACED at the rear handle (2 m behind, facing the carrier's way) and the casualty between them, and he
        /// follows as the carrier turns; a list that names him (another node's) tags him; a list that stops naming him releases him.
        /// ✅ Red-proof: EmbarkedStateSystem without the assistant pass ⇒ he stays where he stood ⇒ red.
        /// </summary>
        [Fact]
        public void TA_TheSecondMan_WalksAtTheRearHandle_AsTheCarrierTurns()
        {
            var carrier = Soldier(100f, new Vector3(10f, 0f, 0f));
            var casualty = Soldier(20f, new Vector3(10f, 0f, 0f));
            var helper = Soldier(100f, new Vector3(11f, 0f, 0f));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, casualty, carrier, mode: CarryMode.Stretcher, assistant: helper));
            var sys = new EmbarkedStateSystem();

            sys.Execute(_world, 0.016f);
            Assert.Equal(new Vector3(8f, 0f, 0f), _world.GetComponentRO<SimTransform>(helper).Position);     // heading +X ⇒ 2 m behind
            Assert.Equal(9f, _world.GetComponentRO<SimTransform>(casualty).Position.X, 3);                   // between them

            _world.GetComponentRW<SimTransform>(carrier).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);   // facing +Y
            sys.Execute(_world, 0.016f);
            var h = _world.GetComponentRO<SimTransform>(helper).Position;
            Assert.Equal(10f, h.X, 3);
            Assert.Equal(-2f, h.Y, 3);

            // another node's view: the list alone names him ⇒ tagged there; the list stops naming him ⇒ released
            _world.RemoveComponent<IsAssistingTag>(helper);
            sys.Execute(_world, 0.016f);
            Assert.Equal(carrier, _world.GetComponentRO<IsAssistingTag>(helper).Carrier);
            _world.GetComponentRW<PassengerBuffer>(carrier).Assistant = Entity.Null;
            sys.Execute(_world, 0.016f);
            Assert.False(_world.HasComponent<IsAssistingTag>(helper));
            Assert.Equal(MoveAndFire, Caps(helper) & MoveAndFire);
        }

        /// <summary>
        /// ⭐ T6 — EITHER man down puts the load down where it is (the casualty stays down, alive) and drops the link; the one still standing
        /// gets his fire back. ✅ Red-proof: CarryAndKill checking only the carrier ⇒ the stretcher goes on with a dead man at the rear ⇒ red.
        /// </summary>
        [Fact]
        public void TA_EitherManDown_PutsTheLoadDown_AndDropsTheLink()
        {
            foreach (bool frontFalls in new[] { false, true })
            {
                var carrier = Soldier(100f);
                var casualty = Soldier(20f);
                var helper = Soldier(100f, new Vector3(1f, 0f, 0f));
                Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, casualty, carrier, mode: CarryMode.Stretcher, assistant: helper));
                _world.GetComponentRW<Health>(frontFalls ? carrier : helper).Current = 0f;
                var sys = new EmbarkedStateSystem();
                sys.Execute(_world, 0.016f);
                sys.Execute(_world, 0.016f);

                Assert.Equal(1, sys.Dropped);
                Assert.False(_world.HasComponent<IsEmbarkedTag>(casualty));
                Assert.True(CombatLife.IsAlive(_world, casualty));
                Assert.True(_world.GetComponentRO<PassengerBuffer>(carrier).Assistant.IsNull);
                Assert.False(_world.HasComponent<IsAssistingTag>(helper));
                var standing = frontFalls ? helper : carrier;
                Assert.Equal(ActorCapabilities.CanShoot, Caps(standing) & ActorCapabilities.CanShoot);
            }
        }

        /// <summary>⭐ T3 — a stretcher slows the pair to 0.6 of the carrier's speed; a carried weapon by its OWN carry factor (a two-man one is
        /// the pair's), never the casualty's 0.5. ✅ Red-proof: CarryLoad without the stretcher case ⇒ 0.5 ⇒ red.</summary>
        [Fact]
        public void TA_AStretcher_Slows_ToSixTenths_AWeapon_ToItsOwnCarryFactor()
        {
            var carrier = Soldier(100f);
            Embarkation.TryEmbark(_world, Soldier(20f), carrier, mode: CarryMode.Stretcher, assistant: Soldier(100f, new Vector3(1f, 0f, 0f)));
            Assert.Equal(0.6f, CarryLoad.Factor(_world, carrier), 3);

            var gunner = Soldier(100f, new Vector3(0f, 20f, 0f));
            Embarkation.TryEmbark(_world, Weapon(WeaponPortability.TwoMan), gunner, assistant: Soldier(100f, new Vector3(1f, 20f, 0f)));
            Assert.Equal(new WeaponSystemDto().CarrySpeedFactor, CarryLoad.Factor(_world, gunner), 3);
        }
    }
}
