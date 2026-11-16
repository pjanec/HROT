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
                    foreach (var e in W.Repo.ReadManagedEvents<AssignTacticalIntentEvent>())
                        if (e != null && e.IntentId == BehaviorNames.CombatPosture) Posts[e.Entity] = Objective(e.JsonParams);
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
            Assert.True(f.Posts[f.Men[3]].Y > 0f && f.Posts[f.Men[4]].Y < 0f);
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
    }
}
