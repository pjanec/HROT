using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;
using Xunit;

using NavResult = Fdp.Toolkit.Navigation.NavigationResult;

namespace CarKinem.Tests.Systems
{
    /// <summary>
    /// ⭐ Buildings Stage 5d-3/5d-4 (📄 docs/DESIGN_Building_Interiors.md §3j "5d") — an agent on a planned path through a doorway: it
    /// stops at a CLOSED door, opens it (action time, one command, the owner's answer), and walks on; a LOCKED door ahead makes the
    /// route stale, so it replans — or fails the move when its intent allows no replan.
    /// </summary>
    public sealed class DoorPassageSystemTests
    {
        // A closed 10 x 8 m room at (20,20) whose only way in is one door ("front", 1.2 m) in the south wall: centre (25, 20).
        private const string OneDoorRoom = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
              {"type":"Feature","properties":{"kind":"building","label":"R","doors":{"front":"closed"},"building":{
                 "footprint":[[0,0],[10,0],[10,8],[0,8]],
                 "storeys":[{"height":3,"walls":[
                      {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},
                      {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
               "geometry":{"type":"Point","coordinates":[20,20]}}]}
            """;

        private sealed class Fixture : System.IDisposable
        {
            public readonly EntityRepository Repo = new();
            public readonly TrajectoryPoolManager Pool = new();
            public readonly DoorPassageSystem System;
            public readonly DoorCommandSystem Owner = new();
            public readonly Entity Agent, Door;

            public Fixture(TerrainDoorState door, float agentY, bool allowReplan = true)
            {
                var r = Repo;
                r.RegisterComponent<NavState>(); r.RegisterComponent<SimTransform>(); r.RegisterComponent<DoorState>();
                r.RegisterComponent<NavigationIntent>(); r.RegisterComponent<NavigationStatus>(); r.RegisterComponent<FrustrationTicks>();
                r.RegisterManagedComponent<TerrainObjectKey>();
                r.RegisterEvent<DoorCommandEvent>(); r.RegisterEvent<PathfindingRequestEvent>(); r.RegisterEvent<PathReplannedEvent>();
                r.RegisterEvent<NavigationPathDetailsResponseEvent>(); r.RegisterEvent<MoveCompletedEvent>();
                r.SetSingletonManaged(TerrainWorldParser.Parse(OneDoorRoom, "range"));

                // the planner's path: outside → doorway (two Door corners) → inside
                Pool.RegisterTrajectoryWithKey(
                    new[] { new Vector3(25, 10, 0), new Vector3(25, 19.8f, 0), new Vector3(25, 20.2f, 0), new Vector3(25, 25, 0) }, 5,
                    new byte[] { 0, (byte)TraversalKind.Door, (byte)TraversalKind.Door, 0 });
                System = new DoorPassageSystem(Pool);

                Door = r.CreateEntity();
                r.AddComponent(Door, new DoorState { State = door });
                r.AddComponent(Door, new SimTransform { Position = new Vector3(25, 20, 0), Rotation = Quaternion.Identity });
                r.SetManagedComponent(Door, new TerrainObjectKey { Key = "range/R/front" });   // no authority component: owned here

                Agent = r.CreateEntity();
                r.AddComponent(Agent, new SimTransform { Position = new Vector3(25, agentY, 0), Rotation = Quaternion.Identity });
                r.AddComponent(Agent, new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = 5, ProgressS = agentY - 10f });
                r.AddComponent(Agent, new NavigationIntent
                {
                    Mode = NavigationMode.PathToPoint, FinalDestination = new Vector3(25, 25, 0), ArrivalRadius = 1f, IntentId = 1,
                    Flags = (byte)(allowReplan ? 1 << NavigationConstants.FlagBitAllowReplan : 0),
                });
                r.AddComponent(Agent, new NavigationStatus { IntentId = 1, Result = NavResult.InProgress });
                r.AddComponent(Agent, new FrustrationTicks());
            }

            /// <summary>One frame: the passage system, then (as on the door's owner) the applier, then the bus moves on.</summary>
            public int Tick(float dt = 0.1f)
            {
                System.Execute(Repo, dt);
                Repo.Bus.SwapBuffers();
                int commands = Repo.Bus.Read<DoorCommandEvent>().Length;
                Owner.Execute(Repo, dt);
                return commands;
            }

            public bool Held => Repo.GetComponent<NavState>(Agent).IsBlocked != 0;
            public TerrainDoorState DoorNow => Repo.GetComponent<DoorState>(Door).State;

            public void Dispose() { Pool.Dispose(); Repo.Dispose(); }
        }

        /// <summary>⭐⭐ 5d-3 — a closed door ahead: held at the door, ONE Open after the action time, released when it is open.</summary>
        [Fact]
        public void Stage5d_AClosedDoorAhead_HoldsTheAgent_OpensItOnce_AndReleasesItWhenOpen()
        {
            using var f = new Fixture(TerrainDoorState.Closed, agentY: 18.5f);   // 1.5 m from the door's centre: in reach
            int commands = 0;
            for (int i = 0; i < 9; i++) commands += f.Tick();                    // 0.9 s < 1 s of opening
            Assert.True(f.Held);
            Assert.Equal(0, commands);
            Assert.Equal(TerrainDoorState.Closed, f.DoorNow);

            for (int i = 0; i < 3; i++) commands += f.Tick();
            Assert.Equal(1, commands);                                           // exactly one command
            Assert.Equal(TerrainDoorState.Open, f.DoorNow);                      // applied by the (local) owner
            Assert.False(f.Held);                                                // walks on
            Assert.Equal(1, f.System.OpenCommandsSent);
            Assert.Equal(NavResult.InProgress, f.Repo.GetComponent<NavigationStatus>(f.Agent).Result);
        }

        /// <summary>⭐ 5d-3 — a closed door still out of reach does not stop the agent yet; an open door never does.</summary>
        [Fact]
        public void Stage5d_ADoorNotYetInReach_OrOpen_DoesNotHold()
        {
            using (var far = new Fixture(TerrainDoorState.Closed, agentY: 16.5f))   // 3.5 m away, the mark already in look-ahead
            {
                for (int i = 0; i < 20; i++) Assert.Equal(0, far.Tick());
                Assert.False(far.Held);
            }
            using var open = new Fixture(TerrainDoorState.Open, agentY: 18.5f);
            for (int i = 0; i < 20; i++) Assert.Equal(0, open.Tick());
            Assert.False(open.Held);
        }

        /// <summary>
        /// ⭐⭐ 5d-4 (R-218 P3) — a door ahead that is LOCKED makes the route stale: ONE replan request through the shared Muscle replan
        /// (not one per frame), the agent is not held, and the move goes on.
        /// </summary>
        [Fact]
        public void Stage5d_ALockedDoorAhead_ReplansOnce()
        {
            using var f = new Fixture(TerrainDoorState.Locked, agentY: 16f);
            int requests = 0;
            for (int i = 0; i < 10; i++)                                          // 1 s, inside the quiet window
            {
                f.System.Execute(f.Repo, 0.1f);
                f.Repo.Bus.SwapBuffers();
                requests += f.Repo.Bus.Read<PathfindingRequestEvent>().Length;
            }
            Assert.Equal(1, requests);
            Assert.Equal(1, f.System.DoorReplans);
            Assert.False(f.Held);
            var status = f.Repo.GetComponent<NavigationStatus>(f.Agent);
            Assert.Equal(1, status.ReplanCount);
            Assert.Equal(NavResult.InProgress, status.Result);
        }

        /// <summary>⭐ 5d-4 — a locked door ahead on a move that may not replan fails the move (FailedBlocked) rather than walking through it.</summary>
        [Fact]
        public void Stage5d_ALockedDoorAhead_WithNoReplanAllowed_FailsTheMove()
        {
            using var f = new Fixture(TerrainDoorState.Locked, agentY: 16f, allowReplan: false);
            f.Tick();
            Assert.Equal(NavResult.FailedBlocked, f.Repo.GetComponent<NavigationStatus>(f.Agent).Result);
            Assert.Equal(0, f.System.DoorReplans);
        }
    }
}
