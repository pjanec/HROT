using System.Linq;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Events;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> D-10 (user-approved `2026-11-19`; 📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §6a/§6b) — the
    /// vehicle SOP's two side steps: A the commander's hatch (out while calm, in on a threat, out after 30 s quiet; one ask per change) and B the
    /// light discipline (the standing one, an order's while it runs, the standing one again after).
    /// </summary>
    public sealed class VehicleCrewNodesTests : System.IDisposable
    {
        private readonly EntityRepository _w = new();
        private double _now;

        public VehicleCrewNodesTests()
        {
            _w.RegisterComponent<PassengerBuffer>();
            _w.RegisterComponent<CarrierSeats>();
            _w.RegisterComponent<RecentSenses>();
            _w.RegisterComponent<VehicleLights>();
            _w.RegisterComponent<BehaviorState>();
            _w.RegisterManagedComponent<BehaviorStartRecord>();
            _w.RegisterEvent<OpeningRequestEvent>();
            _w.RegisterEvent<LightsRequestEvent>();
            At(0);
        }

        public void Dispose() => _w.Dispose();

        private void At(double t) { _now = t; _w.SetSingleton(new GlobalTime { TotalTime = _now, TimeScale = 1f }); }

        private (Entity Vehicle, Entity Commander) Tank()
        {
            var tank = _w.CreateEntity();
            var commander = _w.CreateEntity();
            var buffer = new PassengerBuffer();
            buffer.Passengers[1] = commander;
            _w.AddComponent(tank, buffer);
            var seats = new CarrierSeats();
            seats.Roles[0] = SeatRole.Driver; seats.Roles[1] = SeatRole.Commander;
            _w.AddComponent(tank, seats);
            _w.AddComponent(tank, new RecentSenses());
            return (tank, commander);
        }

        private OpeningRequestEvent[] Hatches() { _w.Bus.SwapBuffers(); var r = _w.Bus.Read<OpeningRequestEvent>().ToArray(); return r; }
        private LightsRequestEvent[] Lights() { _w.Bus.SwapBuffers(); var r = _w.Bus.Read<LightsRequestEvent>().ToArray(); return r; }

        /// <summary>
        /// A — calm: the commander is asked OUT once (a second tick asks nothing); a near miss: IN; 29 s later still in (nothing asked); 31 s after
        /// it, OUT again. ✅ Red-proof: <c>Threatened</c> always false ⇒ the IN row red.
        /// </summary>
        [Fact]
        public void CE4670_D10A_TheCommanderIsOutWhileCalm_InOnAThreat_OutAgainAfterAQuietSpell()
        {
            var (tank, commander) = Tank();
            var p = new CrewHatchParams();

            Assert.Equal(NodeStatus.Success, VehicleCrewNodes.HatchByThreat(ref p, tank, _w));
            var ask = Assert.Single(Hatches());
            Assert.Equal(commander, ask.Man);
            Assert.Equal(OpeningVerb.HeadOut, ask.Verb);
            VehicleCrewNodes.HatchByThreat(ref p, tank, _w);
            Assert.Empty(Hatches());                                                   // one ask per change

            At(10);
            _w.GetComponentRW<RecentSenses>(tank).Record(SensorChange.NearMiss, 10);
            VehicleCrewNodes.HatchByThreat(ref p, tank, _w);
            Assert.Equal(OpeningVerb.HeadIn, Assert.Single(Hatches()).Verb);

            At(39);
            VehicleCrewNodes.HatchByThreat(ref p, tank, _w);
            Assert.Empty(Hatches());                                                   // 29 s: still buttoned up

            At(41);
            VehicleCrewNodes.HatchByThreat(ref p, tank, _w);
            Assert.Equal(OpeningVerb.HeadOut, Assert.Single(Hatches()).Verb);
        }

        /// <summary>A — a vehicle with no man in a Commander seat asks nothing.</summary>
        [Fact]
        public void CE4670_D10A_NoCommander_NoAsk()
        {
            var (tank, _) = Tank();
            _w.GetComponentRW<PassengerBuffer>(tank).Passengers[1] = Entity.Null;
            var p = new CrewHatchParams();
            VehicleCrewNodes.HatchByThreat(ref p, tank, _w);
            Assert.Empty(Hatches());
        }

        private void RunOrder(Entity v, uint instance, string json)
        {
            _w.AddComponent(v, new BehaviorState { InstanceId = instance, Origin = BehaviorOrigin.Superior, ActiveBehaviorHash = 7 });
            _w.SetManagedComponent(v, new BehaviorStartRecord { BehaviorName = "PlatformMove", JsonParams = json, InstanceId = instance });
        }

        /// <summary>
        /// B — the authored discipline (Blackout here) stands: nothing asked; an order carrying <c>"Lights":"Convoy"</c> ⇒ Convoy asked once; the
        /// order ends (an SOP task) ⇒ Blackout asked back. ✅ Red-proof: the override never read ⇒ the Convoy row red.
        /// </summary>
        [Fact]
        public void CE4670_D10B_AnOrdersLights_OverrideTheStandingDiscipline_WhileItRuns()
        {
            var (tank, _) = Tank();
            _w.AddComponent(tank, new VehicleLights { Discipline = LightDiscipline.Blackout });
            var p = new CrewLightsParams();

            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);
            Assert.Empty(Lights());                                                    // the standing discipline: nothing to ask

            RunOrder(tank, 5, "{\"Location\":[100,0,0],\"Lights\":\"Convoy\"}");
            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);
            Assert.Equal(LightDiscipline.Convoy, Assert.Single(Lights()).Discipline);
            _w.GetComponentRW<VehicleLights>(tank).Discipline = LightDiscipline.Convoy; // the owner applied it
            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);
            Assert.Empty(Lights());

            _w.GetComponentRW<BehaviorState>(tank) = new BehaviorState { InstanceId = 6, Origin = BehaviorOrigin.Sop, ActiveBehaviorHash = 9 };
            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);
            Assert.Equal(LightDiscipline.Blackout, Assert.Single(Lights()).Discipline);  // the standing one again
            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);                             // in flight: not taken for an outside change
            Assert.Empty(Lights());
            _w.GetComponentRW<VehicleLights>(tank).Discipline = LightDiscipline.Blackout;
            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);
            Assert.Empty(Lights());
        }

        /// <summary>B — an order WITHOUT <c>Lights</c> changes nothing; a trigger that blacks the vehicle out becomes its new standing discipline.</summary>
        [Fact]
        public void CE4670_D10B_AnOrderWithoutLights_KeepsTheStanding_AnOutsideChangeBecomesIt()
        {
            var (tank, _) = Tank();
            _w.AddComponent(tank, new VehicleLights { Discipline = LightDiscipline.Normal });
            var p = new CrewLightsParams();
            RunOrder(tank, 5, "{\"Location\":[100,0,0]}");
            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);
            Assert.Empty(Lights());

            _w.GetComponentRW<BehaviorState>(tank) = new BehaviorState { InstanceId = 6, Origin = BehaviorOrigin.Sop, ActiveBehaviorHash = 9 };
            _w.GetComponentRW<VehicleLights>(tank).Discipline = LightDiscipline.Blackout;   // a trigger
            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);
            VehicleCrewNodes.LightsByOrder(ref p, tank, _w);
            Assert.Empty(Lights());                                                      // kept: it is the standing one now
        }
    }
}
