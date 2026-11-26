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

        // ── ⭐ CE-4670 VA-S5 — "pop smoke" ──

        private const long TankType = 9900, SmokeGrenade = 9901;

        /// <summary>A tank of a type whose mount 0 is its gun and mount 1 its smoke dischargers (<paramref name="salvos"/> salvos); a launch
        /// warning store, the fire intent on the bus.</summary>
        private Entity SmokeTank(int salvos = 2, bool dischargers = true)
        {
            _w.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            _w.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();
            _w.RegisterComponent<Fdp.Toolkit.Combat.Components.MissileWarning>();
            _w.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            _w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            _w.RegisterComponent<SimTransform>();
            _w.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireIntent>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var type = new Fdp.Interfaces.TkbTemplate("tank", TankType);
            var suite = new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto();
            suite.Mounts.Add(new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun, Range = 3000f });
            if (dischargers)
                suite.Mounts.Add(new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmokeDischarger,
                    AmmoGuid = SmokeGrenade, InitialAmmunition = salvos, MuzzleVelocity = 30f, Range = 30f, Pellets = 8, PelletSpreadMils = 1955f });
            type.AddDescriptor(suite);
            db.Register(type);
            var grenade = new Fdp.Interfaces.TkbTemplate("vehicle smoke grenade", SmokeGrenade);
            grenade.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto { Kind = Fdp.Toolkit.Tkb.Domain.WarheadKind.Smoke, Indirect = true,
                CloudRadiusM = 10f, CloudHeightM = 6f, CloudSeconds = 60f, SmokeBlocksInfrared = true });
            db.Register(grenade);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var (tank, _) = Tank();
            _w.AddComponent(tank, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (TkbTypeId)TankType });
            _w.AddComponent(tank, new SimTransform { Position = new System.Numerics.Vector3(0f, 0f, 0f) });
            _w.AddComponent(tank, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 40, MaxAmmo = 40 });
            if (dischargers)
            {
                var tubes = _w.CreateEntity();
                _w.AddComponent(tubes, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = tank, InstanceId = 1 });
                _w.AddComponent(tubes, new Fdp.Toolkit.Combat.Components.WeaponMountInfo { MountIndex = 1 });
                _w.AddComponent(tubes, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = salvos, MaxAmmo = salvos, MuzzleVelocity = 30f });
            }
            return tank;
        }

        private void Launch(Entity tank, double t, float fromX)
        {
            At(t);
            var w = new Fdp.Toolkit.Combat.Components.MissileWarning { At = (SimInstant)t, From = new System.Numerics.Vector3(fromX, 0f, 1f) };
            if (_w.HasComponent<Fdp.Toolkit.Combat.Components.MissileWarning>(tank)) _w.GetComponentRW<Fdp.Toolkit.Combat.Components.MissileWarning>(tank) = w;
            else _w.AddComponent(tank, w);
            _w.GetComponentRW<RecentSenses>(tank).Record(SensorChange.MissileWarning, t);
        }

        private Fdp.Toolkit.Combat.Events.WeaponFireIntent[] Salvos()
        { _w.Bus.SwapBuffers(); return _w.Bus.Read<Fdp.Toolkit.Combat.Events.WeaponFireIntent>().ToArray(); }

        /// <summary>
        /// A missile launched at the tank from the east ⇒ ONE salvo of its dischargers (mount 1, at the launcher's point); the next tick
        /// nothing; a second launch 10 s later nothing (the cool-down); one 35 s after the first ⇒ a second salvo; a launch the crew learned of
        /// 4 s ago (stale) ⇒ nothing. ✅ Red-proofs: the cool-down test removed ⇒ the t 20 row red; the freshness test removed ⇒ the stale row red.
        /// </summary>
        [Fact]
        public void CE4670_PopSmoke_ALaunchAtTheTank_FiresOneSalvoAtTheLauncher_ThenACooldown()
        {
            var tank = SmokeTank(salvos: 3);
            var p = new CrewSmokeParams();

            Launch(tank, 10, 1200f);
            Assert.Equal(NodeStatus.Success, VehicleCrewNodes.SmokeByThreat(ref p, tank, _w));
            var salvo = Assert.Single(Salvos());
            Assert.Equal(1, salvo.WeaponIndex);
            Assert.True(salvo.AtPoint);
            Assert.Equal(1200f, salvo.TargetPoint.X, 0.01f);
            VehicleCrewNodes.SmokeByThreat(ref p, tank, _w);
            Assert.Empty(Salvos());                                                    // one salvo per launch

            Launch(tank, 20, 1200f);
            VehicleCrewNodes.SmokeByThreat(ref p, tank, _w);
            Assert.Empty(Salvos());                                                    // the cool-down

            Launch(tank, 45, 1200f);
            VehicleCrewNodes.SmokeByThreat(ref p, tank, _w);
            Assert.Single(Salvos());

            Launch(tank, 80, 1200f);
            At(84);                                                                    // learned 4 s ago: history
            VehicleCrewNodes.SmokeByThreat(ref p, tank, _w);
            Assert.Empty(Salvos());
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4698</c> — a survived HIT is screened only by a hull that can back out of the cloud: while its fight-from-cover cycle STANDS AND
        /// FIGHTS (<c>FiringPositionMemory.NoHide</c>) the hit fires nothing; with a hide to go to the same hit fires the salvo at the threat; a LAUNCH
        /// still smokes either way. 📐 <c>ua-tank-ammo</c>: smoked in the open desert, the M1 sat blind in its own cloud past 60 s.
        /// 🔴 Red-proof: the NoHide gate removed ⇒ the standing hull smokes ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE4698_ASurvivedHit_SmokesOnlyAHullThatCanBackOutOfSight_ALaunchAlways()
        {
            var tank = SmokeTank(salvos: 3);
            _w.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
            var mem = new Fdp.Toolkit.Perception.Components.TargetMemory();
            Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, -1, 800f, 0f, 10f, 1);   // the threat, heard at (800, 0)
            mem.Anonymous[0] = 1;
            _w.AddComponent(tank, mem);
            void Hit(double t) { At(t); _w.GetComponentRW<RecentSenses>(tank).Record(SensorChange.Hit, t); }

            UnitMemory.Ref<Fdp.Toolkit.Combat.FiringPositionMemory>(_w, tank).NoHide = 1;   // standing and fighting: nowhere to back out
            var p = new CrewSmokeParams();
            Hit(10);
            VehicleCrewNodes.SmokeByThreat(ref p, tank, _w);
            Assert.Empty(Salvos());                                                    // the cloud would blind its own gun
            Launch(tank, 11, 1200f);
            VehicleCrewNodes.SmokeByThreat(ref p, tank, _w);
            Assert.Single(Salvos());                                                   // a launch still smokes

            UnitMemory.Ref<Fdp.Toolkit.Combat.FiringPositionMemory>(_w, tank).NoHide = 0;   // a hide to back into
            var q = new CrewSmokeParams();
            Hit(50);
            VehicleCrewNodes.SmokeByThreat(ref q, tank, _w);
            var salvo = Assert.Single(Salvos());
            Assert.Equal(800f, salvo.TargetPoint.X, 0.01f);                            // at the threat
        }

        /// <summary>A vehicle with no dischargers, or with none left, pops nothing (and the gun's mount 0 is never fired).</summary>
        [Fact]
        public void CE4670_PopSmoke_NoDischargers_OrNoneLeft_FiresNothing()
        {
            var bare = SmokeTank(dischargers: false);
            var p = new CrewSmokeParams();
            Launch(bare, 10, 1200f);
            VehicleCrewNodes.SmokeByThreat(ref p, bare, _w);
            Assert.Empty(Salvos());
        }
    }
}
