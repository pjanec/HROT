using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Squad.Maneuvers;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-4 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-1, CP-1a, CP-2) — the SHIPPED <c>Checkpoint</c> play through the real
    /// ingress and brain tick: a squad of five on an approach route drawn from (-300, 0) to the stop line at the origin (traffic comes in eastward).
    /// </summary>
    public sealed class CheckpointBehaviorTests
    {
        private const int RouteNet = 7701;

        private sealed class Fixture
        {
            public readonly StationWeaponBehaviorTests.World W = new();
            public readonly Entity[] Men = new Entity[5];
            public readonly Dictionary<Entity, Vector3> Posts = new();

            public Fixture()
            {
                var repo = W.Repo;
                if (!repo.IsComponentTypeRegistered<UnitRoster>()) repo.RegisterComponent<UnitRoster>();
                if (!repo.IsComponentTypeRegistered<CheckpointWatch>()) repo.RegisterComponent<CheckpointWatch>();
                if (!repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>()) repo.RegisterComponent<global::CarKinem.Core.VehicleState>();
                var roster = new UnitRoster { Count = Men.Length };
                for (int i = 0; i < Men.Length; i++)
                {
                    Men[i] = repo.CreateEntity();
                    repo.AddComponent(Men[i], new SimTransform { Position = new Vector3(20f + i, -10f, 0f), Rotation = Quaternion.Identity });
                    repo.AddComponent(Men[i], new Fdp.Toolkit.Perception.Components.TargetMemory());
                    roster.SubordinateEntities[i] = Men[i];
                }
                repo.AddComponent(W.Man, roster);
                if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.SquadCognitiveState>()) repo.RegisterComponent<Fdp.Toolkit.Squad.SquadCognitiveState>();
                repo.AddComponent(W.Man, new Fdp.Toolkit.Squad.SquadCognitiveState());   // a squad's commander
                var route = repo.CreateEntity();
                repo.AddComponent(route, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = RouteNet });
                repo.SetManagedComponent(route, new RoutePlan().WithWaypoints(wps =>
                {
                    wps.Add(new RouteWaypoint { Position = new Vector3(-300f, 0f, 0f) });
                    wps.Add(new RouteWaypoint { Position = new Vector3(-100f, 0f, 0f) });
                    wps.Add(new RouteWaypoint { Position = Vector3.Zero });
                }));
            }

            public void Tick(int k = 1)
            {
                for (int t = 0; t < k; t++)
                {
                    W.Tick();
                    Now += 0.1;
                    foreach (var e in W.Repo.ReadManagedEvents<AssignTacticalIntentEvent>())
                    {
                        if (e == null) continue;
                        if (e.IntentId == BehaviorNames.CombatPosture) Posts[e.Entity] = Objective(e.JsonParams);
                        if (e.IntentId is BehaviorNames.FireAtPoint or BehaviorNames.Suppress) Fire.Add((Now, e.Entity, e.IntentId));
                    }
                }
            }

            /// <summary>A vehicle every man SEES at <paramref name="at"/>, moving at <paramref name="velocity"/>.</summary>
            public Entity Car(Vector3 at, Vector3 velocity)
            {
                var repo = W.Repo;
                var e = repo.CreateEntity();
                repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                repo.AddComponent(e, new SimVelocity { Linear = velocity });
                repo.AddComponent(e, new global::CarKinem.Core.VehicleState());
                foreach (var m in Men)
                {
                    ref var mem = ref repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(m);
                    Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, (long)e.PackedValue, at.X, at.Y, 1f, repo.SimulationTick);
                }
                return e;
            }

            /// <summary>Every moving car drives on by its velocity for one tick (0.1 s), and every man sees where it is.</summary>
            public void Drive(Entity car)
            {
                var repo = W.Repo;
                ref var t = ref repo.GetComponentRW<SimTransform>(car);
                t.Position += repo.GetComponentRO<SimVelocity>(car).Linear * 0.1f;
                foreach (var m in Men)
                {
                    ref var mem = ref repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(m);
                    Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, (long)car.PackedValue, t.Position.X, t.Position.Y, 1f, repo.SimulationTick);
                }
            }

            public readonly List<(double T, Entity Who, string What)> Fire = new();
            public double Now;

            public CheckpointWatch Watch => W.Repo.GetComponentRO<CheckpointWatch>(W.Man);
            public byte Role(int i) => W.Repo.GetComponentRO<Fdp.Toolkit.Squad.SquadCognitiveState>(W.Man).Roles[i].RoleId;
        }

        private static Vector3 Objective(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var a = doc.RootElement.GetProperty("advance").GetProperty("Objective");
            return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
        }

        /// <summary>⭐ CP-1 / CP-1a — every man to his post: the stopper beside the lane at the stop line, two searchers at the bay just past it (both
        /// exposed), the machine gun (no man carries one here: the last) and the reaction at the STAND-OFF behind it — 50 m, the explosive charge's
        /// default when the TKB has no warhead for it. ✅ Red-proof: the stand-off left out of the rear posts ⇒ red.</summary>
        [Fact]
        public void CE4622_K4_TheSquadMansTheCheckpoint_StopperAtTheLine_SearchersAtTheBay_TheRestAtTheStandOff()
        {
            var f = new Fixture();
            f.W.Start(BehaviorNames.Checkpoint, "{\"Approach\":" + RouteNet + "}");
            f.Tick(4);
            Assert.Equal(5, f.Posts.Count);
            Assert.Equal(CheckpointManeuver.RoleStopper, f.Role(0));
            Assert.Equal(CheckpointManeuver.RoleSearcher, f.Role(1));
            Assert.Equal(CheckpointManeuver.RoleSearcher, f.Role(2));
            Assert.Equal(CheckpointManeuver.RoleReaction, f.Role(3));
            Assert.Equal(CheckpointManeuver.RoleOverwatch, f.Role(4));
            Assert.Equal(new Vector3(0f, -CheckpointBehavior.StopperOffsetM, 0f), f.Posts[f.Men[0]]);                    // at the line, beside the lane
            Assert.InRange(f.Posts[f.Men[1]].X, 5f, 11f);                                                                // the bay, just past the line
            Assert.InRange(f.Posts[f.Men[2]].X, 5f, 11f);
            Assert.Equal(CheckpointBehavior.DefaultStandOffM, f.Posts[f.Men[4]].X, 1);                                   // the MG at the stand-off
            Assert.Equal(CheckpointBehavior.DefaultStandOffM, f.Posts[f.Men[3]].X, 1);                                   // the reaction too, the other side
            Assert.True(f.Posts[f.Men[4]].Y > 0f && f.Posts[f.Men[3]].Y < 0f);   // the MG on the far side of the lane from the stopper (y −4)
            Assert.Null(f.W.Finished);                                                                                   // a checkpoint holds
        }

        /// <summary>⭐ CP-2 — the watch: nothing on the road ⇒ none; a car at 18 m/s 30 m from the line ⇒ a THREAT, 1.7 s out, counted once; a car at
        /// 2 m/s 10 m out ⇒ ROUTINE. ✅ Red-proof: the threat count not kept ⇒ red.</summary>
        [Fact]
        public void CE4622_K4_TheWatch_AFastCarIsAThreatByTime_ASlowOneAtTheLineIsRoutine()
        {
            var f = new Fixture();
            f.W.Start(BehaviorNames.Checkpoint, "{\"Approach\":" + RouteNet + "}");
            f.Tick(3);
            Assert.Equal((byte)ApproachVerdict.None, f.Watch.Verdict);
            var fast = f.Car(new Vector3(-30f, 0f, 0f), new Vector3(18f, 0f, 0f));
            f.Tick(2);
            Assert.Equal((byte)ApproachVerdict.Threat, f.Watch.Verdict);
            Assert.Equal((long)fast.PackedValue, f.Watch.Vehicle);
            Assert.InRange(f.Watch.SecondsToStandOff, 1.6f, 1.7f);
            Assert.Equal(1, f.Watch.Threats);
            f.Tick(3);
            Assert.Equal(1, f.Watch.Threats);                                                                            // one car, counted once
            f.W.Repo.DestroyEntity(fast);
            f.Car(new Vector3(-10f, 1f, 0f), new Vector3(2f, 0f, 0f));
            f.Tick(2);
            Assert.Equal((byte)ApproachVerdict.Routine, f.Watch.Verdict);
            Assert.Equal(1, f.Watch.Routines);
        }
    

        /// <summary>⭐⭐ K-5 (CP-3, R-2132) — a car at 15 m/s that never slows, from 18 s out (a 15 s warning line): signalled inside the line (the
        /// stopper's gesture), a THREAT once it has ignored the signal to the warning-shot time; then the MACHINE GUN alone fires the warning shot,
        /// then disabling bursts (by 6 s out), and everyone fires at the car by 3 s out — no shot before the warning-shot time.
        /// ✅ Red-proofs: the ignored-signal rule dropped (the brake test alone: no rung before ~2 s) ⇒ red · the signal not shown ⇒ red.</summary>
        [Fact]
        public void CE4622_K5_ACarThatIgnoresTheSignal_IsLadderedByTime_ShotThenEngineThenLethal()
        {
            var f = new Fixture();
            if (!f.W.Repo.IsComponentTypeRegistered<Gesture>()) f.W.Repo.RegisterComponent<Gesture>();
            f.W.Start(BehaviorNames.Checkpoint, "{\"Approach\":" + RouteNet + "}");
            f.Tick(3);
            var car = f.Car(new Vector3(-270f, 0f, 0f), new Vector3(15f, 0f, 0f));
            double t0 = f.Now, signalAt = -1, threatAt = -1;
            byte maxRung = 0;
            for (int i = 0; i < 175 && f.W.Repo.GetComponentRO<SimTransform>(car).Position.X < -10f; i++)
            {
                f.Drive(car);
                f.Tick();
                if (signalAt < 0 && Gestures.Of(f.W.Repo, f.Men[0]) == GestureKind.Warning) signalAt = f.Now - t0;
                if (threatAt < 0 && f.Watch.Verdict == (byte)ApproachVerdict.Threat) threatAt = f.Now - t0;
                maxRung = System.Math.Max(maxRung, f.Watch.Rung);
            }
            Assert.InRange(signalAt, 2.8, 3.6);                                     // inside the warning line (15 s out = 3 s in)
            Assert.InRange(threatAt, 7.8, 8.6);                                     // ignored the signal to 10 s out
            Assert.Equal((byte)VehicleRung.Lethal, maxRung);
            double firstShot = f.Fire.Count > 0 ? f.Fire[0].T - t0 : -1;
            Assert.InRange(firstShot, 7.8, 8.8);
            Assert.Equal(f.Men[4], f.Fire[0].Who);                                  // the machine gun
            Assert.Equal(BehaviorNames.FireAtPoint, f.Fire[0].What);
            Assert.Contains(f.Fire, x => x.Who == f.Men[4] && x.What == BehaviorNames.FireAtPoint && x.T - t0 >= 11.8 && x.T - t0 < 15.2);   // the engine
            Assert.Contains(f.Fire, x => x.Who == f.Men[0] && x.What == BehaviorNames.Suppress && x.T - t0 >= 14.8);                       // lethal: all
            Assert.DoesNotContain(f.Fire, x => x.Who != f.Men[4] && x.T - t0 < 14.8);                                                  // nobody else earlier
        }

        /// <summary>⭐ K-5 — a car that SLOWS on the signal (15 → 7 m/s, then holds) is never shot at, though it comes inside the warning-shot time at
        /// over 3 m/s: signal only (to 15 m out, where 7 m/s still stops with an ordinary brake). ✅ Red-proof: the slowing test dropped ⇒ red.</summary>
        [Fact]
        public void CE4622_K5_ACarThatSlowsOnTheSignal_IsNeverShotAt()
        {
            var f = new Fixture();
            if (!f.W.Repo.IsComponentTypeRegistered<Gesture>()) f.W.Repo.RegisterComponent<Gesture>();
            f.W.Start(BehaviorNames.Checkpoint, "{\"Approach\":" + RouteNet + "}");
            f.Tick(3);
            var car = f.Car(new Vector3(-250f, 0f, 0f), new Vector3(15f, 0f, 0f));
            for (int i = 0; i < 400 && f.W.Repo.GetComponentRO<SimTransform>(car).Position.X < -15f; i++)
            {
                ref var v = ref f.W.Repo.GetComponentRW<SimVelocity>(car);
                if (f.Watch.Signalling != 0 && v.Linear.X > 7f) v.Linear = new Vector3(v.Linear.X - 0.2f, 0f, 0f);   // brakes ~2 m/s² on the signal, to 7 m/s
                f.Drive(car);
                f.Tick();
            }
            Assert.Equal(1, f.Watch.Signalling);
            Assert.Equal(0, f.Watch.Threats);
            Assert.Empty(f.Fire);
        }
    

        /// <summary>A car STOPPED at the line with a driver aboard (seat 0) and, when <paramref name="charge"/>, a thing in its concealed boot (seat 2).</summary>
        private static (Entity Car, Entity Driver) StoppedCar(Fixture f, bool charge)
        {
            var repo = f.W.Repo;
            foreach (var reg in new System.Action[]
            {
                () => { if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.Custody>()) repo.RegisterComponent<Fdp.Toolkit.Civilians.Custody>(); },
                () => { if (!repo.Bus.IsRegistered<Fdp.Toolkit.Civilians.CustodyEvent>()) repo.Bus.Register<Fdp.Toolkit.Civilians.CustodyEvent>(); },
                () => { if (!repo.IsComponentTypeRegistered<Gesture>()) repo.RegisterComponent<Gesture>(); },
            }) reg();
            var car = f.Car(new Vector3(-4f, 0f, 0f), Vector3.Zero);
            var driver = repo.CreateEntity();
            repo.AddComponent(driver, new SimTransform { Position = new Vector3(-4f, 0f, 0f), Rotation = Quaternion.Identity });
            var buffer = new PassengerBuffer { Count = charge ? 2 : 1 };
            buffer.Passengers[0] = driver;
            repo.AddComponent(driver, new IsEmbarkedTag { VehicleEntity = car, Seat = 0 });
            if (charge)
            {
                var thing = repo.CreateEntity();
                buffer.Passengers[2] = thing;
                repo.AddComponent(thing, new IsEmbarkedTag { VehicleEntity = car, Seat = 2 });
                repo.AddComponent(car, new CarrierStations { StationMask = 1 << 2, StowedMask = 1 << 2, ConcealedMask = 1 << 2 });
            }
            repo.AddComponent(car, buffer);
            return (car, driver);
        }

        /// <summary>⭐⭐ K-6 (CP-4) — a car stopped at the line: its driver is told "out, hands up" (a REACTION, never a write), the vehicle searcher
        /// goes to its side; once the driver is out showing his hands and the searchers are in reach, each asks custody to search — the man and the
        /// vehicle; both searched and nothing in the boot ⇒ RELEASED: he is told to board again. ✅ Red-proof: the vehicle's search never asked ⇒ red.</summary>
        [Fact]
        public void CE4622_K6_AStoppedCar_IsSearched_ManAndVehicle_ThenReleased()
        {
            var f = new Fixture();
            f.W.Start(BehaviorNames.Checkpoint, "{\"Approach\":" + RouteNet + "}");
            f.Tick(3);
            var demands = new List<(Entity To, string What, BehaviorOrigin Origin)>();
            string? embarkJson = null;
            var (car, driver) = StoppedCar(f, charge: false);
            void Run(int k)
            {
                for (int i = 0; i < k; i++)
                {
                    f.Drive(car);
                    f.W.Tick();
                    foreach (var e in f.W.Repo.ReadManagedEvents<AssignTacticalIntentEvent>())
                    {
                        if (e == null) continue;
                        if (e.IntentId == BehaviorNames.CombatPosture) f.Posts[e.Entity] = Objective(e.JsonParams);
                        if (e.Entity == driver) demands.Add((e.Entity, e.IntentId, e.Origin));
                        if (e.Entity == driver && e.IntentId == BehaviorNames.Embark) embarkJson = e.JsonParams;
                    }
                }
            }
            Run(3);
            Assert.Equal(1, f.Watch.SearchStage);
            Assert.Contains(demands, d => d.What == BehaviorNames.ComplyAtCheckpoint && d.Origin == BehaviorOrigin.Reaction);
            Assert.InRange(Vector3.Distance(f.Posts[f.Men[2]], new Vector3(-4f, 0f, 0f)), 2f, 3f);         // the vehicle searcher, at its side
            // he gets out with his hands up; the searchers are where they were sent
            var repo = f.W.Repo;
            repo.RemoveComponent<IsEmbarkedTag>(driver);
            repo.GetComponentRW<SimTransform>(driver).Position = new Vector3(-4f, -2.5f, 0f);
            Gestures.Set(repo, driver, GestureKind.HandsUp);
            Run(3);
            repo.GetComponentRW<SimTransform>(f.Men[1]).Position = new Vector3(-4f, -3f, 0f);
            repo.GetComponentRW<SimTransform>(f.Men[2]).Position = f.Posts[f.Men[2]];
            var asks = new List<Fdp.Toolkit.Civilians.CustodyEvent>();
            for (int i = 0; i < 3; i++) { Run(1); foreach (var a in repo.Bus.Read<Fdp.Toolkit.Civilians.CustodyEvent>()) asks.Add(a); }
            Assert.Contains(asks, a => a.Man == driver && a.By == f.Men[1] && a.Verb == Fdp.Toolkit.Civilians.CustodyVerb.Search);
            Assert.Contains(asks, a => a.Man == car && a.By == f.Men[2] && a.Verb == Fdp.Toolkit.Civilians.CustodyVerb.Search);
            // custody finishes both searches (its system's job): nothing found ⇒ released, he boards again
            repo.AddComponent(driver, new Fdp.Toolkit.Civilians.Custody { Searched = 1 });
            repo.AddComponent(car, new Fdp.Toolkit.Civilians.Custody { Searched = 1 });
            Run(3);
            Assert.Equal((2, 1, 0), (f.Watch.SearchStage, f.Watch.Released, f.Watch.Held));
            // ⭐ the release is an INSTRUCTION (a reaction is refused by his running "hands up") and names the vehicle by Embark's own key
            Assert.Contains(demands, d => d.What == BehaviorNames.Embark && d.Origin == BehaviorOrigin.Superior);
            Assert.Contains("\"vehicle\":", embarkJson);
        }

        /// <summary>⭐ K-6 — the same search of a car with something in its concealed boot ⇒ FOUND, the driver HELD (detained), never told to board.
        /// ✅ Red-proof: the boot not counted at the demand ⇒ released ⇒ red.</summary>
        [Fact]
        public void CE4622_K6_ACarWithSomethingInItsBoot_IsHeld()
        {
            var f = new Fixture();
            f.W.Start(BehaviorNames.Checkpoint, "{\"Approach\":" + RouteNet + "}");
            f.Tick(3);
            var (car, driver) = StoppedCar(f, charge: true);
            var repo = f.W.Repo;
            var asks = new List<Fdp.Toolkit.Civilians.CustodyEvent>();
            void Run(int k) { for (int i = 0; i < k; i++) { f.Drive(car); f.Tick(); foreach (var a in repo.Bus.Read<Fdp.Toolkit.Civilians.CustodyEvent>()) asks.Add(a); } }
            Run(3);
            Assert.Equal(1, f.Watch.SearchStage);
            repo.RemoveComponent<IsEmbarkedTag>(driver);
            repo.AddComponent(driver, new Fdp.Toolkit.Civilians.Custody { Searched = 1 });
            repo.AddComponent(car, new Fdp.Toolkit.Civilians.Custody { Searched = 1 });
            repo.GetComponentRW<SimTransform>(f.Men[1]).Position = new Vector3(-4f, -1f, 0f);
            repo.GetComponentRW<SimTransform>(driver).Position = new Vector3(-4f, -1.5f, 0f);
            Run(3);
            Assert.Equal((3, 1, 0, 1), (f.Watch.SearchStage, f.Watch.Found, f.Watch.Released, f.Watch.Held));
            Assert.Contains(asks, a => a.Man == driver && a.Verb == Fdp.Toolkit.Civilians.CustodyVerb.Detain);
        }

        // ── ⭐ R-2133's ⏭ — the signalled vehicle stops at the line on its own (StopAtSignal) ─────────────────────────────────────────────

        private static void Arrive(StationWeaponBehaviorTests.World w)
        {
            ref var ch = ref w.Repo.GetComponentRW<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(w.Man);
            ch.Status = NodeStatus.Success;
        }

        /// <summary>⭐ The signalled vehicle closes on the LINE ON ITS OWN COURSE at <see cref="StopAtSignalBehavior.ApproachSpeed"/> and stops there; it holds while its people
        /// are out (the search) and ends once they are all back in (released) — the task it paused then goes on. ✅ Red-proofs: the move not issued ⇒ red;
        /// "released" ending as soon as one gets out ⇒ red.</summary>
        [Fact]
        public unsafe void R2133_ASignalledVehicle_StopsAtTheLine_HoldsWhileSearched_EndsWhenItsPeopleAreBack()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            var driver = repo.CreateEntity();
            var buffer = new PassengerBuffer(); buffer.Passengers[0] = driver;
            repo.AddComponent(w.Man, buffer);
            var p = new StopAtSignalBehavior.StopAtSignalParams { StopAt = new Vector3(380f, 12f, 0f) };
            var s = new StopAtSignalBehavior.StopAtSignalState();
            w.Tick();
            Assert.Equal(NodeStatus.Running, StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo));
            var ch = repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(w.Man);
            Assert.Equal(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo, ch.ActiveAction);
            {
                byte* b = ch.Params;                                                                 // a local: its fixed buffer is already fixed
                var mp = *(Fdp.Toolkit.Navigation.MoveToParams*)b;
                Assert.Equal(380f - StopAtSignalBehavior.CreepMetres, mp.Destination.X, 2);         // ON its course (it faces +X from the origin), short of the line
                Assert.Equal(0f, mp.Destination.Y, 2);
                Assert.Equal(StopAtSignalBehavior.ApproachSpeed, mp.Speed);
            }
            Arrive(w); w.Tick();
            Assert.Equal(NodeStatus.Running, StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo));   // the creep to the line
            {
                var ch2 = repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(w.Man);
                byte* b2 = ch2.Params;
                var mp2 = *(Fdp.Toolkit.Navigation.MoveToParams*)b2;
                Assert.Equal(380f, mp2.Destination.X, 2);
                Assert.Equal(StopAtSignalBehavior.CreepSpeed, mp2.Speed);
            }
            Arrive(w); w.Tick();
            Assert.Equal(NodeStatus.Running, StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo));   // stopped at the line
            Assert.Equal(1, s.Phase);
            ref var bf = ref repo.GetComponentRW<PassengerBuffer>(w.Man);
            bf.Passengers[0] = Entity.Null;                                                          // "out, hands up"
            for (int i = 0; i < 300; i++) { w.Tick(); Assert.Equal(NodeStatus.Running, StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo)); }   // 30 s searched
            repo.GetComponentRW<PassengerBuffer>(w.Man).Passengers[0] = driver;                     // released: he boards again
            w.Tick();
            Assert.Equal(NodeStatus.Success, StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo));
        }

        /// <summary>⭐ Stopped at the line and nobody asked out: after <see cref="StopAtSignalBehavior.WavedOnSeconds"/> it is waved on (Success).
        /// ✅ Red-proof: the waved-on end dropped ⇒ it waits for ever ⇒ red.</summary>
        [Fact]
        public void R2133_ASignalledVehicle_NobodyAskedOut_IsWavedOn()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            var buffer = new PassengerBuffer(); buffer.Passengers[0] = repo.CreateEntity();
            repo.AddComponent(w.Man, buffer);
            var p = new StopAtSignalBehavior.StopAtSignalParams { StopAt = new Vector3(380f, 12f, 0f) };
            var s = new StopAtSignalBehavior.StopAtSignalState();
            w.Tick(); StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo);
            Arrive(w); w.Tick(); StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo);
            Arrive(w); w.Tick(); StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo);
            var last = NodeStatus.Running;
            for (int i = 0; i < (int)(StopAtSignalBehavior.WavedOnSeconds * 10) - 5; i++) { w.Tick(); last = StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo); }
            Assert.Equal(NodeStatus.Running, last);
            for (int i = 0; i < 10; i++) { w.Tick(); last = StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo); }
            Assert.Equal(NodeStatus.Success, last);
        }

        /// <summary>⭐ A vehicle under a RAM order ignores the signal: the reaction ends at once and issues no move (📐 cp-ram-barrier: the rammer obeyed and
        /// never rammed). ✅ Red-proof: the ram check dropped ⇒ it closes on the line ⇒ red.</summary>
        [Fact]
        public void R2133_AVehicleUnderARamOrder_IgnoresTheSignal()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<global::CarKinem.Core.RamOrder>()) repo.RegisterComponent<global::CarKinem.Core.RamOrder>();
            repo.AddComponent(w.Man, new global::CarKinem.Core.RamOrder { Active = 1 });
            var p = new StopAtSignalBehavior.StopAtSignalParams { StopAt = new Vector3(380f, 12f, 0f) };
            var s = new StopAtSignalBehavior.StopAtSignalState();
            w.Tick();
            Assert.Equal(NodeStatus.Success, StopAtSignalBehavior.Tick(ref p, ref s, w.Man, repo));
            Assert.NotEqual(Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo, repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(w.Man).ActiveAction);
        }
    }
}
