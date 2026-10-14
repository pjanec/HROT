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
    }
}
