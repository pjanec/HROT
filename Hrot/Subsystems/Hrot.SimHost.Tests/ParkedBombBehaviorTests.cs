using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-5 (IM-2; docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §5 / §9) — the SHIPPED <c>ParkedBomb</c> through the real
    /// ingress and brain tick: the vehicle rides to the spot (its move channel), and only once it has ARRIVED it arms the cargo charge's
    /// timer and puts its riders out (one <see cref="EjectPassengersCommand"/>), Success; the charge then goes off on its timer through
    /// <see cref="ChargeSystem"/> (asked by no one), not before. 🔴 Red-proof: arm before arriving (drop the move-status wait) ⇒ red.
    /// </summary>
    public sealed class ParkedBombBehaviorTests
    {
        private const int ChargeNetId = 5611;

        [Fact]
        public void CE4621_ParkedBomb_RidesToTheSpot_ThenArmsTheTimer_AndPutsTheRidersOut_TheChargeGoesOffOnTime()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<ChargeTimer>()) repo.RegisterComponent<ChargeTimer>();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            if (!repo.Bus.IsRegistered<DetonateChargeEvent>()) repo.Bus.Register<DetonateChargeEvent>();
            if (!repo.Bus.IsRegistered<EjectPassengersCommand>()) repo.Bus.Register<EjectPassengersCommand>();
            var charge = repo.CreateEntity();
            repo.AddComponent(charge, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(charge, new WeaponState { Ammo = 1 });
            var t = new TkbTemplate("Bike bomb", 5612L);
            t.AddDescriptor(new WeaponSystemDto { MountKinds = MountKinds.Ground | MountKinds.Cargo, Portable = WeaponPortability.OneMan, Charge = true });
            new WeaponSystemTkbTranslator().Inject(repo, charge, t);
            repo.AddComponent(charge, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = ChargeNetId });

            w.Start(BehaviorNames.ParkedBomb, "{\"Charge\":" + ChargeNetId + ",\"Spot\":[50,0,0],\"Delay\":20}");
            int ejects = 0;
            for (int i = 0; i < 10; i++) { w.Tick(); foreach (var e in repo.Bus.Read<EjectPassengersCommand>()) if (e.Vehicle == w.Man) ejects++; }
            Assert.Null(w.Finished);                                     // still riding: the channel is Running
            Assert.False(ChargeTimers.IsArmed(repo, charge));
            Assert.Equal(0, ejects);
            ref readonly var loco = ref repo.GetComponentRO<LocomotionChannel>(w.Man);
            Assert.Equal(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo, loco.ActiveAction);

            repo.GetComponentRW<LocomotionChannel>(w.Man).Status = NodeStatus.Success;   // it has arrived
            for (int i = 0; i < 3; i++) { w.Tick(); foreach (var e in repo.Bus.Read<EjectPassengersCommand>()) if (e.Vehicle == w.Man) ejects++; }
            Assert.Equal(NodeStatus.Success, w.Finished);
            Assert.True(ChargeTimers.IsArmed(repo, charge));
            Assert.Equal(1, ejects);
            double at = (double)repo.GetComponentRO<ChargeTimer>(charge).DetonateAt;
            double now = repo.GetSingleton<GlobalTime>().TotalTime;
            Assert.InRange(at - now, 19.5, 20.01);

            var charges = new ChargeSystem();
            charges.Execute(repo, 0.1f);
            Assert.Equal(0, charges.TimersFired);                        // not before its time
            repo.SetSingleton(new GlobalTime { TotalTime = at + 0.05 });
            charges.Execute(repo, 0.1f);
            Assert.Equal(1, charges.TimersFired);
            repo.Bus.SwapBuffers();
            int asks = 0;
            foreach (var e in repo.Bus.Read<DetonateChargeEvent>()) if (e.Charge == charge && e.By.IsNull) asks++;
            Assert.Equal(1, asks);                                       // asked by no one (the burst itself is ChargesTests' rail)
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-5 (IM-1, MV-12) — the SHIPPED <c>DriveBy</c> on a bike through the real ingress and brain tick: it rides up to the
    /// target at speed; within the pass distance it orders its PILLION (the firing seat) to <c>FireAtTarget</c> and passes the target at the
    /// slow pass speed, beyond it; then it escapes to the pre-chosen point at speed and finishes there. 🔴 Red-proof: the pass at the ride-up
    /// speed (PassSpeed ignored) ⇒ red.
    /// </summary>
    public sealed unsafe class DriveByBehaviorTests
    {
        private const int TargetNetId = 5621;

        private static Fdp.Toolkit.Navigation.MoveToParams Move(EntityRepository repo, Entity e)
        {
            var ch = repo.GetComponentRO<LocomotionChannel>(e);
            return *(Fdp.Toolkit.Navigation.MoveToParams*)ch.Params;
        }

        [Fact]
        public void CE4621_DriveBy_RidesUp_PassesSlowlyWhileThePillionIsOrderedToFire_ThenEscapesAtSpeed()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var seats = new TkbTemplate("Bike", 5620L);
            seats.AddDescriptor(new CarrierDefDto
            {
                Exposure = PassengerExposure.Open,
                Seats =
                {
                    new SeatDto { Role = SeatRole.Driver, Exposed = true, TraverseMinDeg = -120f, TraverseMaxDeg = 120f, FiresStoppedOnly = true },
                    new SeatDto { Role = SeatRole.Passenger, Exposed = true, X = -0.45f, Z = 0.85f, TraverseMinDeg = 30f, TraverseMaxDeg = 330f },
                },
            });
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(repo, w.Man, seats);
            var rider = repo.CreateEntity();
            var pillion = repo.CreateEntity();
            repo.GetComponentRW<PassengerBuffer>(w.Man).Passengers[0] = rider;
            repo.GetComponentRW<PassengerBuffer>(w.Man).Passengers[1] = pillion;
            Assert.Equal(pillion, Hrot.AI.Behaviors.Brains.DriveByBehavior.PillionOf(repo, w.Man));

            var target = repo.CreateEntity();
            repo.AddComponent(target, new SimTransform { Position = new Vector3(200f, 0f, 0f), Rotation = Quaternion.Identity });
            repo.AddComponent(target, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = TargetNetId });

            w.Start(BehaviorNames.DriveBy, "{\"Target\":" + TargetNetId + ",\"Escape\":[-300,0,0]}");
            int orders = 0;
            void Tick() { w.Tick(); foreach (var e in repo.ReadManagedEvents<AssignBehaviorEvent>()) if (e != null && e.Entity == pillion && e.BehaviorName == BehaviorNames.FireAtTarget) orders++; }
            for (int i = 0; i < 4; i++) Tick();
            var up = Move(repo, w.Man);
            Assert.Equal(200f, up.Destination.X, 1);
            Assert.Equal(Hrot.AI.Behaviors.Brains.DriveByBehavior.DefaultSpeed, up.Speed, 2);
            Assert.Equal(0, orders);

            repo.GetComponentRW<SimTransform>(w.Man).Position = new Vector3(170f, 0f, 0f);   // inside the pass distance
            for (int i = 0; i < 3; i++) Tick();
            var pass = Move(repo, w.Man);
            Assert.Equal(200f + Hrot.AI.Behaviors.Brains.DriveByBehavior.BeyondMetres, pass.Destination.X, 1);
            Assert.Equal(Hrot.AI.Behaviors.Brains.DriveByBehavior.DefaultPassSpeed, pass.Speed, 2);
            Assert.Equal(1, orders);                                     // ONE order to the pillion, never one per tick

            repo.GetComponentRW<LocomotionChannel>(w.Man).Status = NodeStatus.Success;          // past the target
            for (int i = 0; i < 3; i++) Tick();
            var away = Move(repo, w.Man);
            Assert.Equal(-300f, away.Destination.X, 1);
            Assert.Equal(Hrot.AI.Behaviors.Brains.DriveByBehavior.DefaultSpeed, away.Speed, 2);
            Assert.Null(w.Finished);
            repo.GetComponentRW<LocomotionChannel>(w.Man).Status = NodeStatus.Success;          // got away
            for (int i = 0; i < 3; i++) Tick();
            Assert.Equal(NodeStatus.Success, w.Finished);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-6 (IM-5) — the SHIPPED <c>SwarmRaid</c> on a commander of three bikes: each is ordered to its OWN approach point
    /// (three bearings 120° apart), then all converge on the objective, hold for the fight, ride OUT on divergent bearings and on to the
    /// rally point — Success there. A phase waits for every live bike. 🔴 Red-proof: one shared point for every bike (no bearing) ⇒ red.
    /// </summary>
    public sealed class SwarmRaidBehaviorTests
    {
        [Fact]
        public void CE4621_SwarmRaid_ConvergesFromThreeBearings_Fights_ThenDispersesOnDivergentRoutesToTheRally()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitRoster>()) repo.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster { Count = 3 };
            var bikes = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                bikes[i] = repo.CreateEntity();
                repo.AddComponent(bikes[i], new SimTransform { Position = new Vector3(-1000f, 0f, 0f), Rotation = Quaternion.Identity });
                roster.SubordinateEntities[i] = bikes[i];
            }
            repo.AddComponent(w.Man, roster);
            var p = new Hrot.AI.Behaviors.Brains.SwarmRaidBehavior.SwarmRaidParams
            {
                Objective = Vector3.Zero, Rally = new Vector3(0f, -800f, 0f), ApproachMetres = 300f, Fight = (SimDuration)10f, Speed = 14f,
            };
            var last = new System.Collections.Generic.Dictionary<Entity, string>();
            void Tick(int k = 1)
            {
                for (int t = 0; t < k; t++)
                {
                    w.Tick();
                    foreach (var e in repo.ReadManagedEvents<AssignTacticalIntentEvent>())
                        if (e != null && e.IntentId == BehaviorNames.MoveToLocation) last[e.Entity] = e.JsonParams;
                }
            }
            Vector3 Ordered(int i)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(last[bikes[i]]);
                var a = doc.RootElement.GetProperty("target");
                return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
            }
            void Arrive(byte phase) { for (int i = 0; i < 3; i++) repo.GetComponentRW<SimTransform>(bikes[i]).Position = Hrot.AI.Behaviors.Brains.SwarmRaidBehavior.PointOf(in p, phase, i, 3); }

            w.Start(BehaviorNames.SwarmRaid, "{\"Objective\":[0,0,0],\"Rally\":[0,-800,0],\"ApproachMetres\":300,\"Fight\":10}");
            Tick(3);
            Assert.Equal(3, last.Count);
            for (int i = 0; i < 3; i++) Assert.Equal(300f, new Vector2(Ordered(i).X, Ordered(i).Y).Length(), 0);   // on the ring…
            Assert.True(Vector3.Distance(Ordered(0), Ordered(1)) > 400f && Vector3.Distance(Ordered(1), Ordered(2)) > 400f);   // …120° apart
            Tick(5);
            Assert.Equal(300f, new Vector2(Ordered(0).X, Ordered(0).Y).Length(), 0);   // a phase waits for every bike

            Arrive(Hrot.AI.Behaviors.Brains.SwarmRaidBehavior.Approach); Tick(3);
            for (int i = 0; i < 3; i++) Assert.True(new Vector2(Ordered(i).X, Ordered(i).Y).Length() <= Hrot.AI.Behaviors.Brains.SwarmRaidBehavior.ConvergeMetres + 0.1f);
            Arrive(Hrot.AI.Behaviors.Brains.SwarmRaidBehavior.Converge); Tick(3);
            var held = Ordered(0);
            Tick(50);                                                     // 5 s of the 10 s fight: no new order
            Assert.Equal(held, Ordered(0));
            Tick(60);                                                     // the fight is over: out on divergent bearings
            for (int i = 0; i < 3; i++) Assert.Equal(450f, new Vector2(Ordered(i).X, Ordered(i).Y).Length(), 0);
            Assert.True(Vector3.Distance(Ordered(0), Ordered(1)) > 600f);
            Arrive(Hrot.AI.Behaviors.Brains.SwarmRaidBehavior.DisperseOut); Tick(3);
            for (int i = 0; i < 3; i++) Assert.True(Vector3.Distance(Ordered(i), p.Rally) <= 20.1f);
            Assert.Null(w.Finished);
            Arrive(Hrot.AI.Behaviors.Brains.SwarmRaidBehavior.DisperseRally); Tick(3);
            Assert.Equal(NodeStatus.Success, w.Finished);
        }
    }
}

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-4 (MC-3…MC-7; docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §9) — the SHIPPED <c>GreyZoneDash</c> and <c>DashRide</c>
    /// through the real ingress and brain tick.
    /// </summary>
    public sealed unsafe class GreyZoneDashBehaviorTests
    {
        private static Fdp.Toolkit.Navigation.MoveToParams Move(EntityRepository repo, Entity e)
        {
            var ch = repo.GetComponentRO<LocomotionChannel>(e);
            return *(Fdp.Toolkit.Navigation.MoveToParams*)ch.Params;
        }

        /// <summary>⭐ both plays FULFIL a Dash ⇒ THE ONE GATE refuses the halting reactions of the commander and of each bike (CD-2/CD-3).
        /// ✅ Red-proof: <c>Fulfils</c> dropped from <c>DashRide</c> ⇒ red.</summary>
        [Fact]
        public void CE4621_MC3_TheDashAndEachBikesRide_AreOrdersOfKindDash_ThatForbidEveryHaltingConduct()
        {
            Hrot.AI.Behaviors.Orders.OrderKinds.Build();
            int H(string n) => Fdp.Toolkit.Behavior.BehaviorHash.FromName(n);
            Assert.Equal(Hrot.AI.Behaviors.Orders.OrderKind.Dash, Hrot.AI.Behaviors.Orders.OrderKinds.Of(H(BehaviorNames.GreyZoneDash)));
            Assert.Equal(Hrot.AI.Behaviors.Orders.OrderKind.Dash, Hrot.AI.Behaviors.Orders.OrderKinds.Of(H(BehaviorNames.DashRide)));
            Assert.Equal(Fdp.Toolkit.Behavior.Conduct.All, Fdp.Toolkit.Behavior.BehaviorConduct.ForbiddenBy(H(BehaviorNames.DashRide)));
            Assert.Equal(Fdp.Toolkit.Behavior.Conduct.All, Fdp.Toolkit.Behavior.BehaviorConduct.ForbiddenBy(H(BehaviorNames.GreyZoneDash)));
        }

        /// <summary>⭐ MC-3 — the commander releases bike 0 at once as a <c>DashRide</c> on the legs, bike 1 only once bike 0 is 80 m off, bike 2
        /// once bike 1 is (one drone, one bike). ✅ Red-proof: the spacing test dropped ⇒ all three released at once ⇒ red.</summary>
        [Fact]
        public void CE4621_MC3_GreyZoneDash_ReleasesEachBikeOnceTheOneAheadIs80mOff()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitRoster>()) repo.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster { Count = 3 };
            var bikes = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                bikes[i] = repo.CreateEntity();
                repo.AddComponent(bikes[i], new SimTransform { Position = new Vector3(-5f * i, 0f, 0f), Rotation = Quaternion.Identity });
                roster.SubordinateEntities[i] = bikes[i];
            }
            repo.AddComponent(w.Man, roster);
            var rides = new System.Collections.Generic.Dictionary<Entity, string>();
            void Tick(int k = 1)
            {
                for (int t = 0; t < k; t++)
                {
                    w.Tick();
                    foreach (var e in repo.ReadManagedEvents<AssignTacticalIntentEvent>())
                        if (e != null && e.IntentId == BehaviorNames.DashRide) rides[e.Entity] = e.JsonParams;
                }
            }
            w.Start(BehaviorNames.GreyZoneDash, "{\"Legs\":[[500,0,0],[1000,0,0]]}");
            Tick(3);
            Assert.Equal(new[] { bikes[0] }, rides.Keys);
            Assert.Contains("[1000,0,0]", rides[bikes[0]]);
            repo.GetComponentRW<SimTransform>(bikes[0]).Position = new Vector3(60f, 0f, 0f);   // 65 m ahead of bike 1: not yet
            Tick(3);
            Assert.Single(rides);
            repo.GetComponentRW<SimTransform>(bikes[0]).Position = new Vector3(80f, 0f, 0f);   // 85 m
            Tick(3);
            Assert.Equal(2, rides.Count);
            Assert.False(rides.ContainsKey(bikes[2]));                                           // bike 2 waits for bike 1
            repo.GetComponentRW<SimTransform>(bikes[1]).Position = new Vector3(80f, 0f, 0f);
            Tick(3);
            Assert.Equal(3, rides.Count);
            Assert.Null(w.Finished);
        }

        private static (StationWeaponBehaviorTests.World W, Entity Rider) Bike()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.TargetMemory>()) repo.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
            if (!repo.Bus.IsRegistered<EjectPassengersCommand>()) repo.Bus.Register<EjectPassengersCommand>();
            var rider = repo.CreateEntity();
            repo.AddComponent(rider, new Fdp.Toolkit.Perception.Components.TargetMemory());
            repo.GetComponentRW<PassengerBuffer>(w.Man).Passengers[0] = rider;
            repo.GetComponentRW<SimTransform>(w.Man).Position = Vector3.Zero;
            return (w, rider);
        }

        private static void Sees(EntityRepository repo, Entity rider, long drone, Vector3 at)
        {
            ref var mem = ref repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(rider);
            mem.Count = 1;
            mem.EntityIds[0] = drone;
            mem.PositionsX[0] = at.X; mem.PositionsY[0] = at.Y; mem.PositionsZ[0] = at.Z;
            mem.Freshness[0] = 1f;
            mem.LastSeenTick[0] = 1;
        }

        /// <summary>⭐⭐ MC-4 — the lookout: a drone in the rider's memory diving on the bike (from the north-east, 30 m/s) is watched; at a
        /// time-to-go of 3 s nothing happens; at ≤ 1 s (the FPV's commit + 0.3 s) the bike breaks HARD — a turn off its own heading, AWAY from
        /// the drone: 40 m to the side and a little ahead, at full speed — then resumes its leg. ✅ Red-proofs: the time-to-go test dropped ⇒ it
        /// breaks at 3 s ⇒ red; the side toward the drone ⇒ red.</summary>
        [Fact]
        public void CE4621_MC4_DashRide_BreaksAcrossADroneDivingOnIt_AtTheCommitTimeToGo_NotBefore()
        {
            var (w, rider) = Bike();
            var repo = w.Repo;
            w.Start(BehaviorNames.DashRide, "{\"Legs\":[[1000,0,0]],\"Speed\":25}");
            for (int i = 0; i < 3; i++) w.Tick();
            var ride = Move(repo, w.Man);
            Assert.Equal(1000f, ride.Destination.X, 1);
            Assert.Equal(25f, ride.Speed, 2);

            // the drone comes in down a line from the south (−Y), closing 3 m a tick (30 m/s); the bike rides east (+X)
            var dir = Vector3.Normalize(new Vector3(0f, 1f, -1f));
            long drone = 777;
            Vector3 At(float range) => -dir * range;
            for (float r = 120f; r > 92f; r -= 3f) { Sees(repo, rider, drone, At(r)); w.Tick(); }   // tgo ≈ 3 s
            Assert.Equal(1000f, Move(repo, w.Man).Destination.X, 1);                             // no break yet
            float range = 92f;
            while (range > 27f) { range -= 3f; Sees(repo, rider, drone, At(range)); w.Tick(); if (Move(repo, w.Man).Destination.X < 900f) break; }
            var brk = Move(repo, w.Man);
            Assert.True(range <= 30f + 3f, $"the break waits for time-to-go ≤ 1 s: broke at {range} m");
            Assert.True(brk.Destination.X < 900f, "it broke");
            Assert.True(MathF.Abs(brk.Destination.X) >= 10f && brk.Destination.X > 0f, $"a little ahead, never back: {brk.Destination}");
            Assert.True(brk.Destination.Y >= 35f, $"a hard turn off its heading, AWAY from the drone (it comes from −Y): {brk.Destination}");
            Assert.Equal(25f, brk.Speed, 2);
            for (int i = 0; i < 26; i++) w.Tick();                                               // the break's 2.5 s
            Assert.Equal(1000f, Move(repo, w.Man).Destination.X, 1);                             // back on its leg
        }

        /// <summary>⭐ MC-6 — at the last leg's end the bike halts and puts its riders out (one <see cref="EjectPassengersCommand"/>), Success.
        /// ✅ Red-proof: no eject ⇒ red.</summary>
        [Fact]
        public void CE4621_MC6_DashRide_AtTheEdge_HaltsAndPutsItsRidersOut()
        {
            var (w, _) = Bike();
            var repo = w.Repo;
            w.Start(BehaviorNames.DashRide, "{\"Legs\":[[300,0,0],[600,0,0]]}");
            int ejects = 0;
            void Tick() { w.Tick(); foreach (var e in repo.Bus.Read<EjectPassengersCommand>()) if (e.Vehicle == w.Man) ejects++; }
            for (int i = 0; i < 3; i++) Tick();
            Assert.Equal(300f, Move(repo, w.Man).Destination.X, 1);
            repo.GetComponentRW<SimTransform>(w.Man).Position = new Vector3(295f, 0f, 0f);
            for (int i = 0; i < 3; i++) Tick();
            Assert.Equal(600f, Move(repo, w.Man).Destination.X, 1);
            Assert.Equal(0, ejects);
            repo.GetComponentRW<SimTransform>(w.Man).Position = new Vector3(596f, 0f, 0f);
            for (int i = 0; i < 4; i++) Tick();
            Assert.Equal(NodeStatus.Success, w.Finished);
            Assert.Equal(1, ejects);
        }
    }
}
