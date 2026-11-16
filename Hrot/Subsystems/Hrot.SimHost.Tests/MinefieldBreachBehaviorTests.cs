using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B3 (Minefields MF-9, MF-11, MR-11; docs/designs/minefields/DESIGN_Minefields.md §11) — the SHIPPED <c>ClearLane</c>,
    /// <c>CrossLane</c> and <c>Breach</c> through the real ingress and brain tick.
    /// </summary>
    public sealed unsafe class MinefieldBreachBehaviorTests
    {
        private static Fdp.Toolkit.Navigation.MoveToParams Move(EntityRepository repo, Entity e)
        {
            var ch = repo.GetComponentRO<LocomotionChannel>(e);
            return *(Fdp.Toolkit.Navigation.MoveToParams*)ch.Params;
        }

        /// <summary>⭐ MF-9 / MR-11 — the clearer drives to the lane's start at its driving speed, then down the lane to its end at the TOOL's pace
        /// (0.5 m/s), and ends there; its own <c>MineClearing</c> marker (terrain's switch, R-2126) is OFF on the way, ON down the lane with the
        /// ordered tool and width, OFF at the end. ✅ Red-proofs: the lane driven at the driving speed ⇒ red; the switch-on dropped ⇒ red.</summary>
        [Fact]
        public void CE4624_MS_B3_ClearLane_DrivesToTheMouth_ThenTheLaneAtTheToolsPace()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.MineClearing>()) repo.RegisterComponent<Fdp.Toolkit.Terrain.MineClearing>();
            repo.AddComponent(w.Man, new Fdp.Toolkit.Terrain.MineClearing());                       // a TKB 116 carries it, inactive
            Fdp.Toolkit.Terrain.MineClearing Tool() => repo.GetComponentRO<Fdp.Toolkit.Terrain.MineClearing>(w.Man);
            w.Start(BehaviorNames.ClearLane, "{\"Start\":[100,0,0],\"End\":[180,0,0],\"Width\":5,\"Tool\":1}");
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(100f, Move(repo, w.Man).Destination.X, 1);
            Assert.Equal(ClearLaneBehavior.DefaultSpeed, Move(repo, w.Man).Speed, 2);
            Assert.Equal(0, Tool().Active);                                                          // off on the way to the mouth
            repo.GetComponentRW<SimTransform>(w.Man).Position = new Vector3(99f, 0f, 0f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(180f, Move(repo, w.Man).Destination.X, 1);
            Assert.Equal(ClearLaneBehavior.DefaultPace, Move(repo, w.Man).Speed, 2);
            Assert.Equal(1, Tool().Active);                                                          // ON down the lane, the ordered tool and swath
            Assert.Equal(Fdp.Toolkit.Terrain.MineClearingTool.Plough, Tool().Tool);
            Assert.Equal(5f, Tool().WidthM);
            Assert.Null(w.Finished);
            repo.GetComponentRW<SimTransform>(w.Man).Position = new Vector3(179.5f, 0f, 0f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(NodeStatus.Success, w.Finished);
            Assert.Equal(0, Tool().Active);                                                          // off at the end
        }

        private static (StationWeaponBehaviorTests.World W, Entity[] Units) Commander(int n, int vehicles = 0)
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<UnitRoster>()) repo.RegisterComponent<UnitRoster>();
            if (vehicles > 0 && !repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>()) repo.RegisterComponent<global::CarKinem.Core.VehicleState>();
            var roster = new UnitRoster { Count = n };
            var units = new Entity[n];
            for (int i = 0; i < n; i++)
            {
                units[i] = repo.CreateEntity();
                repo.AddComponent(units[i], new SimTransform { Position = new Vector3(-30f - i, 3f * i, 0f), Rotation = Quaternion.Identity });
                if (i < vehicles) repo.AddComponent(units[i], new global::CarKinem.Core.VehicleState());
                roster.SubordinateEntities[i] = units[i];
            }
            repo.AddComponent(w.Man, roster);
            return (w, units);
        }

        private static Vector3 Target(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var a = doc.RootElement.GetProperty("target");
            return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
        }

        /// <summary>⭐ MF-11 — a squad crosses the lane in SINGLE FILE: every man first to the assembly point on the lane's axis 10 m short of its start;
        /// then man 1 only once man 0 is 5 m down the lane, man 2 once man 1 is; each straight to the exit 10 m past the end; Success once all are out.
        /// ✅ Red-proof: the spacing wait dropped ⇒ all three sent at once ⇒ red.</summary>
        [Fact]
        public void CE4624_MS_B3_CrossLane_AssemblesOnTheAxis_ThenOneManAtATime_FiveMetresApart()
        {
            var (w, men) = Commander(3);
            var repo = w.Repo;
            var last = new Dictionary<Entity, string>();
            void Tick(int k = 1)
            {
                for (int t = 0; t < k; t++)
                {
                    w.Tick();
                    foreach (var e in repo.ReadManagedEvents<AssignTacticalIntentEvent>())
                        if (e != null && e.IntentId == BehaviorNames.MoveToLocation) last[e.Entity] = e.JsonParams;
                }
            }
            w.Start(BehaviorNames.CrossLane, "{\"Start\":[0,0,0],\"End\":[60,0,0]}");
            Tick(3);
            Assert.Equal(3, last.Count);
            foreach (var m in men) Assert.Equal(new Vector3(-10f, 0f, 0f), Target(last[m]));   // the assembly point, on the axis
            foreach (var m in men) repo.GetComponentRW<SimTransform>(m).Position = new Vector3(-10f, 0f, 0f);
            Tick(3);
            Assert.Equal(new Vector3(70f, 0f, 0f), Target(last[men[0]]));                     // man 0 goes, straight down the axis to the exit
            Assert.Equal(new Vector3(-10f, 0f, 0f), Target(last[men[1]]));                    // man 1 waits
            repo.GetComponentRW<SimTransform>(men[0]).Position = new Vector3(-6f, 0f, 0f);    // 4 m on: not yet
            Tick(3);
            Assert.Equal(new Vector3(-10f, 0f, 0f), Target(last[men[1]]));
            repo.GetComponentRW<SimTransform>(men[0]).Position = new Vector3(-4f, 0f, 0f);    // 6 m on
            Tick(3);
            Assert.Equal(new Vector3(70f, 0f, 0f), Target(last[men[1]]));
            Assert.Equal(new Vector3(-10f, 0f, 0f), Target(last[men[2]]));
            repo.GetComponentRW<SimTransform>(men[1]).Position = new Vector3(-4f, 0f, 0f);
            Tick(3);
            Assert.Equal(new Vector3(70f, 0f, 0f), Target(last[men[2]]));
            Assert.Null(w.Finished);
            foreach (var m in men) repo.GetComponentRW<SimTransform>(m).Position = new Vector3(70f, 0f, 0f);
            Tick(3);
            Assert.Equal(NodeStatus.Success, w.Finished);
        }

        /// <summary>⭐ MF-11 — the breach play: the support unit is ordered <c>SupportByFire</c> on the FAR side (60 m past the lane's end), the vehicle
        /// <c>ClearLane</c> on the lane, and only once the clearer's order has ended is the assault unit (the last foot unit) ordered <c>CrossLane</c>.
        /// ✅ Red-proof: the crossing ordered with the clearing ⇒ red.</summary>
        [Fact]
        public void CE4624_MS_B3_Breach_FiresOnTheFarSide_ClearsTheLane_ThenTheAssaultCrosses()
        {
            var (w, units) = Commander(3, vehicles: 1);   // 0 the clearer, 1 the support, 2 the assault
            var repo = w.Repo;
            var orders = new List<(double T, Entity To, string What, string Json)>();
            double t0 = 0;
            for (int t = 0; t < 60 && w.Finished == null; t++)
            {
                w.Tick();
                foreach (var e in repo.ReadManagedEvents<AssignTacticalIntentEvent>())
                    if (e != null) orders.Add((t * 0.1, e.Entity, e.IntentId, e.JsonParams));
                if (t == 0) w.Start(BehaviorNames.Breach, "{\"Start\":[0,0,0],\"End\":[80,0,0]}");
            }
            var sbf = orders.Find(o => o.What == BehaviorNames.SupportByFire);
            Assert.Equal(units[1], sbf.To);
            var dto = System.Text.Json.JsonSerializer.Deserialize<SupportByFireParamsJsonDto>(sbf.Json, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.Equal(new Vector3(140f, 0f, 0f), dto.Post1);                                   // the far side
            var clear = orders.Find(o => o.What == BehaviorNames.ClearLane);
            Assert.Equal(units[0], clear.To);
            var cross = orders.Find(o => o.What == BehaviorNames.CrossLane);
            Assert.Equal(units[2], cross.To);
            Assert.True(cross.T >= clear.T + 2.0 - 1e-6, $"the crossing waits for the clearing: clear {clear.T} s, cross {cross.T} s");
            Assert.Equal(NodeStatus.Success, w.Finished);
            _ = t0;
        }
    }
}
