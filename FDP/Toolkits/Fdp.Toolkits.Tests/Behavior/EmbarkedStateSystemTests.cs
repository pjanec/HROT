using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2b (R-224, Q-1) — the passenger side DERIVED from the vehicle's list on a node that did not decide the boarding:
    /// the list arrives (here: written directly, as the ingress does), <see cref="EmbarkedStateSystem"/> tags and strips; the list empties,
    /// it untags, gives the capabilities back and places the body beside the hull — but only where the passenger's POSITION is owned.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7.1c.
    /// </summary>
    public sealed class EmbarkedStateSystemTests : IDisposable
    {
        private const int Here = 1, Elsewhere = 2;
        private readonly EntityRepository _world = new();
        private readonly EmbarkedStateSystem _system = new();

        public EmbarkedStateSystemTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterManagedComponent<DescriptorOwnership>();
        }

        public void Dispose() => _world.Dispose();

        private Entity Vehicle(Vector3 at)
        {
            var v = _world.CreateEntity();
            _world.AddComponent(v, new SimTransform { Position = at, Rotation = Quaternion.Identity });   // facing east (+X)
            _world.AddComponent(v, new PassengerBuffer());
            return v;
        }

        /// <summary>A soldier whose position is owned by <paramref name="positionOwner"/> (this node is <see cref="Here"/>).</summary>
        private Entity Soldier(Vector3 at, int positionOwner = Here)
        {
            var s = _world.CreateEntity();
            _world.AddComponent(s, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(s, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            _world.AddComponent(s, new NetworkAuthority(primaryOwnerId: Elsewhere, localNodeId: Here));
            var ownership = new DescriptorOwnership();
            ownership.Map[Embarkation.PositionKey] = positionOwner;
            _world.SetManagedComponent(s, ownership);
            return s;
        }

        /// <summary>The list as the ingress writes it — no tag, no capabilities touched.</summary>
        private void ListArrives(Entity vehicle, params Entity[] passengers)
        {
            var b = new PassengerBuffer();
            foreach (var p in passengers) b.Passengers[b.Count++] = p;
            _world.SetComponent(vehicle, b);
        }

        private ActorCapabilities Caps(Entity e) => _world.GetComponent<ActorCapabilityState>(e).Capabilities;

        [Fact]
        public void CE3110_I2b_AListThatArrives_TagsAndStripsThePassenger()
        {
            var apc = Vehicle(new Vector3(100, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            var b = Soldier(new Vector3(91, 50, 0));

            ListArrives(apc, a, b);
            _system.Execute(_world, 0f);

            foreach (var s in new[] { a, b })
            {
                Assert.Equal(apc, _world.GetComponentRO<IsEmbarkedTag>(s).VehicleEntity);
                Assert.Equal(ActorCapabilities.None, Caps(s) & Embarkation.StrippedAboard);
            }
            Assert.Equal(2, _system.Boarded);

            _system.Execute(_world, 0f);   // nothing new: idempotent
            Assert.Equal(2, _system.Boarded);
        }

        [Fact]
        public void CE3110_I2b_AnEmptiedList_Untags_GivesCapsBack_AndPlacesBesideTheHull_OnlyWhereThePositionIsOwned()
        {
            var apc = Vehicle(new Vector3(100, 50, 0));
            var ours = Soldier(new Vector3(90, 50, 0), positionOwner: Here);
            var theirs = Soldier(new Vector3(91, 50, 0), positionOwner: Elsewhere);
            ListArrives(apc, ours, theirs);
            _system.Execute(_world, 0f);

            ListArrives(apc);
            _system.Execute(_world, 0f);

            foreach (var s in new[] { ours, theirs })
            {
                Assert.False(_world.HasComponent<IsEmbarkedTag>(s));
                Assert.Equal(Embarkation.StrippedAboard, Caps(s) & Embarkation.StrippedAboard);
            }
            // the column on the right (south of an east-facing hull): beside it, not where it boarded
            var placed = _world.GetComponentRO<SimTransform>(ours).Position;
            Assert.Equal(Embarkation.DismountPoint(_world, apc, 0, 2), placed);
            Assert.True(placed.Y < 50f);
            // the other node owns that body's position — a write here would be overwritten next frame, so none is made
            Assert.Equal(new Vector3(91, 50, 0), _world.GetComponentRO<SimTransform>(theirs).Position);
            Assert.Equal(2, _system.Left);
        }

        [Fact]
        public void CE3110_I2b_TheDecidingNode_IsANoOp_ItsTagAndListAgreeAlready()
        {
            var apc = Vehicle(new Vector3(100, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, a, apc));

            _system.Execute(_world, 0f);
            Assert.Equal(0, _system.Boarded);
            Assert.Equal(0, _system.Left);
            Assert.Equal(apc, _world.GetComponentRO<IsEmbarkedTag>(a).VehicleEntity);
        }

        [Fact]
        public void CE3110_I2b_AMoveToAnotherVehicle_RetagsWithoutLeaving()
        {
            var first = Vehicle(new Vector3(100, 50, 0));
            var second = Vehicle(new Vector3(200, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            ListArrives(first, a);
            _system.Execute(_world, 0f);

            ListArrives(first);
            ListArrives(second, a);
            _system.Execute(_world, 0f);

            Assert.Equal(second, _world.GetComponentRO<IsEmbarkedTag>(a).VehicleEntity);
            Assert.Equal(ActorCapabilities.None, Caps(a) & Embarkation.StrippedAboard);
            Assert.Equal(0, _system.Left);
        }

        [Fact]
        public void CE3110_I2b_ADeadPassenger_LeavesWithoutItsCapabilitiesBack()
        {
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var apc = Vehicle(new Vector3(100, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            _world.AddComponent(a, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
            ListArrives(apc, a);
            _system.Execute(_world, 0f);

            ListArrives(apc);
            _system.Execute(_world, 0f);

            Assert.False(_world.HasComponent<IsEmbarkedTag>(a));
            Assert.Equal(ActorCapabilities.None, Caps(a) & Embarkation.StrippedAboard);
        }

        // ── I-2c (R-271) — carried, killed with the vehicle, exposure ──────────────────────────────────────────────

        private void RegisterCombat()
        {
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            _world.RegisterComponent<OpenPassengerCabin>();
            _world.RegisterEvent<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>();
        }

        /// <summary>⭐ <c>CE-3110</c> I-2c — a passenger is CARRIED: where this node owns its position it takes the vehicle's position,
        /// rotation and velocity every frame; where another node owns it, it is left for that node's replicated position.</summary>
        [Fact]
        public void CE3110_I2c_APassenger_IsCarried_WhereItsPositionIsOwned()
        {
            RegisterCombat();
            var apc = Vehicle(new Vector3(100, 50, 0));
            _world.AddComponent(apc, new SimVelocity { Linear = new Vector3(8, 0, 0) });
            var ours = Soldier(new Vector3(90, 50, 0), positionOwner: Here);
            _world.AddComponent(ours, new SimVelocity());
            var theirs = Soldier(new Vector3(91, 50, 0), positionOwner: Elsewhere);
            ListArrives(apc, ours, theirs);
            _system.Execute(_world, 0f);

            var heading = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);
            _world.SetComponent(apc, new SimTransform { Position = new Vector3(140, 60, 0), Rotation = heading });
            _system.Execute(_world, 0f);

            Assert.Equal(new Vector3(140, 60, 0), _world.GetComponentRO<SimTransform>(ours).Position);
            Assert.Equal(heading, _world.GetComponentRO<SimTransform>(ours).Rotation);
            Assert.Equal(new Vector3(8, 0, 0), _world.GetComponentRO<SimVelocity>(ours).Linear);   // dead reckoning on the other nodes
            Assert.Equal(new Vector3(91, 50, 0), _world.GetComponentRO<SimTransform>(theirs).Position);
        }

        /// <summary>
        /// ⭐⭐ Weapon systems WS-2 (R-2016; docs/designs/weapons/DESIGN_Weapon_Systems.md §6) — a weapon in a STATION seat stands at the
        /// seat's place, facing the seat's way, in the host's frame — as the host drives and turns (a plain passenger still rides at the
        /// reference point). The pickup's bed station: 1.5 m behind, 1.4 m up, facing aft. ✅ Red-proof: CarryAndKill without the station
        /// branch ⇒ the M2 sits at the pickup's reference point ⇒ red.
        /// </summary>
        [Fact]
        public void WS2_AStationWeapon_StandsAtItsStation_AsThePickupDrivesAndTurns()
        {
            RegisterCombat();
            _world.RegisterComponent<CarrierStations>();
            var pickup = Vehicle(new Vector3(100, 0, 0));
            var stations = new CarrierStations { StationMask = 0b10 };
            stations.Seats[1] = new StationSeat { Accepts = 2, X = -1.5f, Z = 1.4f, Facing = MathF.PI };
            _world.AddComponent(pickup, stations);
            var m2 = Soldier(new Vector3(0, 0, 0));   // any entity whose position this node owns
            var rider = Soldier(new Vector3(0, 0, 0));
            var b = new PassengerBuffer();
            b.Passengers[0] = rider;
            b.Passengers[1] = m2;
            b.Count = 2;
            _world.SetComponent(pickup, b);
            _system.Execute(_world, 0f);

            static void Near(Vector3 want, Vector3 got) => Assert.True(Vector3.Distance(want, got) < 1e-4f, $"{want} vs {got}");
            Near(new Vector3(98.5f, 0, 1.4f), _world.GetComponentRO<SimTransform>(m2).Position);
            Near(-Vector3.UnitX, Vector3.Transform(Vector3.UnitX, _world.GetComponentRO<SimTransform>(m2).Rotation));   // facing aft (west)
            Near(new Vector3(100, 0, 0), _world.GetComponentRO<SimTransform>(rider).Position);

            var north = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);   // the pickup turns to face north (+Y)
            _world.SetComponent(pickup, new SimTransform { Position = new Vector3(120, 10, 0), Rotation = north });
            _system.Execute(_world, 0f);
            Near(new Vector3(120, 8.5f, 1.4f), _world.GetComponentRO<SimTransform>(m2).Position);
            Near(-Vector3.UnitY, Vector3.Transform(Vector3.UnitX, _world.GetComponentRO<SimTransform>(m2).Rotation));   // still aft: south
        }

        /// <summary>⭐ <c>CE-3110</c> I-2c (R-271: "if vehicle gets destroyed, all inside die") — the vehicle's Health reaching 0 makes the node
        /// that owns a passenger's Health publish ONE lethal damage event for it (into the one Health writer); a passenger whose Health
        /// another node owns gets none from here; the body stays aboard.</summary>
        [Fact]
        public void CE3110_I2c_AVehicleDestroyed_KillsThePassengers_OnTheNodeOwningTheirHealth()
        {
            RegisterCombat();
            var apc = Vehicle(new Vector3(100, 50, 0));
            _world.AddComponent(apc, new Fdp.Toolkit.Combat.Components.Health { Current = 300f, Max = 300f });
            var ours = Soldier(new Vector3(90, 50, 0));
            _world.AddComponent(ours, new Fdp.Toolkit.Combat.Components.Health { Current = 80f, Max = 100f });
            _world.SetAuthority<Fdp.Toolkit.Combat.Components.Health>(ours, true);
            var theirs = Soldier(new Vector3(91, 50, 0));
            _world.AddComponent(theirs, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            _world.SetAuthority<Fdp.Toolkit.Combat.Components.Health>(theirs, false);
            ListArrives(apc, ours, theirs);
            _system.Execute(_world, 0f);
            _world.Bus.SwapBuffers();
            Assert.Empty(_world.Bus.Read<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>().ToArray());   // an intact vehicle kills nobody

            _world.SetComponent(apc, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 300f });
            _system.Execute(_world, 0f);
            _system.Execute(_world, 0f);   // said once, not every frame
            _world.Bus.SwapBuffers();

            var kill = Assert.Single(_world.Bus.Read<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>().ToArray());
            Assert.Equal(ours, kill.HitEntity);
            Assert.Equal(80f, kill.TotalDamage);   // exactly lethal
            Assert.Equal(1, _system.Killed);
            Assert.Equal(apc, _world.GetComponentRO<IsEmbarkedTag>(ours).VehicleEntity);   // the body stays aboard the wreck
        }

        /// <summary>⭐ <c>CE-3110</c> I-2c — boarding an OPEN-cabin vehicle leaves the passenger exposed; an enclosed one hides it.</summary>
        [Fact]
        public void CE3110_I2c_BoardingAnOpenCabin_LeavesThePassengerExposed()
        {
            RegisterCombat();
            var heli = Vehicle(new Vector3(100, 50, 0));
            _world.AddComponent(heli, new OpenPassengerCabin());
            var apc = Vehicle(new Vector3(200, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            var b = Soldier(new Vector3(190, 50, 0));
            ListArrives(heli, a);
            ListArrives(apc, b);
            _system.Execute(_world, 0f);

            Assert.Equal(1, _world.GetComponentRO<IsEmbarkedTag>(a).Exposed);
            Assert.Equal(0, _world.GetComponentRO<IsEmbarkedTag>(b).Exposed);
            Assert.False(Fdp.Toolkit.Perception.PhysicalPresence.IsHidden(_world, a));
            Assert.True(Fdp.Toolkit.Perception.PhysicalPresence.IsHidden(_world, b));
        }
    }
}
