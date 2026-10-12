using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2a (R-224) — THE embarkation rules (<see cref="Embarkation"/>) and their one applier
    /// (<see cref="EmbarkationSystem"/>, the vehicle's owner). Also carries the three claims of the retired <c>EditorCargoSystemTests</c>
    /// (embark fills the list, a full vehicle refuses, disembark removes the tag). 📄 docs/DESIGN_Entity_Interactions.md §7.1a.
    /// </summary>
    public sealed class EmbarkationTests : IDisposable
    {
        private readonly EntityRepository _world = new();
        private readonly EmbarkationSystem _system = new();

        public EmbarkationTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitEntity>();
            _world.RegisterEvent<EmbarkEntityCommand>();
            _world.RegisterEvent<DisembarkEntityCommand>();
            _world.RegisterEvent<EjectPassengersCommand>();
        }

        public void Dispose() => _world.Dispose();

        private Entity Vehicle(Vector3 at)
        {
            var v = _world.CreateEntity();
            _world.AddComponent(v, new SimTransform { Position = at, Rotation = Quaternion.Identity });   // facing east (+X)
            _world.AddComponent(v, new PassengerBuffer());
            return v;
        }

        private Entity Soldier(Vector3 at)
        {
            var s = _world.CreateEntity();
            _world.AddComponent(s, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(s, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            return s;
        }

        private void Frame()
        {
            _world.Bus.SwapBuffers();
            _system.Execute(_world, 0f);
        }

        private ActorCapabilities Caps(Entity e) => _world.GetComponent<ActorCapabilityState>(e).Capabilities;

        /// <summary>Boarding writes all three facts — the vehicle's list, the passenger's tag naming its vehicle, CanMove/CanShoot
        /// gone — and boarding twice is not two seats.</summary>
        [Fact]
        public void CE3110_TryEmbark_FillsTheList_TagsThePassenger_StripsItsCapabilities_AndIsIdempotent()
        {
            var v = Vehicle(Vector3.Zero);
            var s = Soldier(new Vector3(1f, 0f, 0f));

            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, s, v));
            Assert.Equal(EmbarkResult.AlreadyAboard, Embarkation.TryEmbark(_world, s, v));

            var buf = _world.GetComponent<PassengerBuffer>(v);
            Assert.Equal(1, buf.Count);
            Assert.Equal(s, buf.Passengers[0]);
            Assert.Equal(v, _world.GetComponent<IsEmbarkedTag>(s).VehicleEntity);
            Assert.Equal(ActorCapabilities.None, Caps(s) & Embarkation.StrippedAboard);
        }

        /// <summary>
        /// ⭐ A body-less UNIT (a squad's commander, R-272) is not a physical actor: it never takes a seat, and — although every brained
        /// entity carries an empty <see cref="PassengerBuffer"/> — it carries nobody. Both directions refuse, and nothing is written.
        /// </summary>
        [Fact]
        public void CE3110_AUnitNeitherBoardsNorCarries()
        {
            var v = Vehicle(Vector3.Zero);
            var unit = Vehicle(Vector3.Zero);   // a unit: brained, so it has a PassengerBuffer too
            _world.AddComponent(unit, new Fdp.Core.CommandHierarchy.UnitEntity());
            var s = Soldier(Vector3.Zero);

            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, unit, v));
            Assert.Equal(EmbarkResult.Invalid, Embarkation.TryEmbark(_world, s, unit));

            Assert.Equal(0, _world.GetComponent<PassengerBuffer>(v).Count);
            Assert.Equal(0, _world.GetComponent<PassengerBuffer>(unit).Count);
            Assert.False(_world.HasComponent<IsEmbarkedTag>(unit));
            Assert.False(_world.HasComponent<IsEmbarkedTag>(s));
        }

        /// <summary>A full vehicle refuses (was <c>EditorCargoSystemTests.Embark_AtCapacity_DoesNotExceedCapacityLimit</c>).</summary>
        [Fact]
        public void CE3110_AFullVehicleRefuses()
        {
            var v = Vehicle(Vector3.Zero);
            for (int i = 0; i < PassengerBuffer.Capacity; i++) Embarkation.TryEmbark(_world, Soldier(Vector3.Zero), v);
            var extra = Soldier(Vector3.Zero);

            _world.Bus.Publish(new EmbarkEntityCommand { Passenger = extra, Vehicle = v });
            Frame();

            Assert.Equal(PassengerBuffer.Capacity, _world.GetComponent<PassengerBuffer>(v).Count);
            Assert.False(_world.HasComponent<IsEmbarkedTag>(extra));
            Assert.Equal(1, _system.Refused);
        }

        /// <summary>
        /// ⭐ One passenger leaves through the applier (the command names no vehicle: its tag does): out of the list, the tag gone,
        /// the capabilities back — and standing BESIDE the hull on the vehicle's right (CE-321), not inside it where it boarded.
        /// (Was <c>EditorCargoSystemTests.Disembark_AfterEmbark_RemovesIsEmbarkedTag</c>, which left the soldier inside the hull.)
        /// </summary>
        [Fact]
        public void CE3110_DisembarkOne_LeavesTheList_RestoresCapabilities_AndStandsBesideTheHull()
        {
            var v = Vehicle(new Vector3(10f, 10f, 0f));
            var a = Soldier(new Vector3(10f, 10f, 0f));
            var b = Soldier(new Vector3(10f, 10f, 0f));
            _world.Bus.Publish(new EmbarkEntityCommand { Passenger = a, Vehicle = v });
            _world.Bus.Publish(new EmbarkEntityCommand { Passenger = b, Vehicle = v });
            Frame();
            Assert.Equal(2, _world.GetComponent<PassengerBuffer>(v).Count);

            _world.Bus.Publish(new DisembarkEntityCommand { Passenger = a });
            Frame();

            var buf = _world.GetComponent<PassengerBuffer>(v);
            Assert.Equal(1, buf.Count);
            Assert.Equal(b, buf.Passengers[0]);
            Assert.False(_world.HasComponent<IsEmbarkedTag>(a));
            Assert.Equal(Embarkation.StrippedAboard, Caps(a) & Embarkation.StrippedAboard);
            var at = _world.GetComponent<SimTransform>(a).Position;
            // facing east ⇒ its right is south (−Y), hull radius 2 + clearance 1.5
            Assert.Equal(10f - (Embarkation.DefaultHullRadius + Embarkation.DismountClearance), at.Y, 3);
            Assert.True(_world.HasComponent<IsEmbarkedTag>(b), "the other passenger stays aboard");
        }

        /// <summary>⭐ R-224 — only the VEHICLE's owner applies a boarding: on a node that does not own it the command is left for
        /// the transport to carry, and nothing is written here.</summary>
        [Fact]
        public void CE3110_ANodeThatDoesNotOwnTheVehicle_AppliesNothing()
        {
            var v = Vehicle(Vector3.Zero);
            _world.AddComponent(v, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            var s = Soldier(Vector3.Zero);

            _world.Bus.Publish(new EmbarkEntityCommand { Passenger = s, Vehicle = v });
            Frame();

            Assert.Equal(0, _world.GetComponent<PassengerBuffer>(v).Count);
            Assert.False(_world.HasComponent<IsEmbarkedTag>(s));
            Assert.False(Embarkation.OwnsVehicle(_world, v));
        }

        /// <summary>Eject through the applier: every live passenger out in a column beside the hull, the dead skipped, the list
        /// empty.</summary>
        [Fact]
        public void CE3110_EjectAll_ThroughTheApplier_EmptiesTheVehicle()
        {
            var v = Vehicle(Vector3.Zero);
            var a = Soldier(Vector3.Zero);
            var dead = Soldier(Vector3.Zero);
            Embarkation.TryEmbark(_world, a, v);
            Embarkation.TryEmbark(_world, dead, v);
            _world.DestroyEntity(dead);

            _world.Bus.Publish(new EjectPassengersCommand { Vehicle = v });
            Frame();

            Assert.Equal(0, _world.GetComponent<PassengerBuffer>(v).Count);
            Assert.False(_world.HasComponent<IsEmbarkedTag>(a));
            Assert.Equal(Embarkation.StrippedAboard, Caps(a) & Embarkation.StrippedAboard);
        }
    }
}
