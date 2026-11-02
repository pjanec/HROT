using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// ⭐ Macros S3a ⑤ (docs/DESIGN_Scenario_Macros.md §14.2 SR-181 / SR-182, §14.4 SR-190) — <see cref="PlacementSolverSystem"/>: Exact on the navmesh ·
    /// Shifted within MaxShift · Failed beyond · Air always Exact · spacing between slots and from existing entities · the terrain-only water ring
    /// search. The full event pipeline: publish the managed request, swap, solve, playback, swap, read the managed response.
    /// 🔴 Mutation: ignore MaxShift (drop the <c>Distance2D(result, want) &gt; maxShift</c> test) ⇒ the Failed-beyond rail goes red.
    /// </summary>
    public sealed class PlacementSolverSystemTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public PlacementSolverSystemTests()
        {
            _world.RegisterManagedEvent<PlacementRequestBatch>();
            _world.RegisterManagedEvent<PlacementResponseBatch>();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<global::CarKinem.Core.VehicleParams>();
        }

        public void Dispose() => _world.Dispose();

        /// <summary>Walkable only inside the box [min, max] (XY); anything else projects to the nearest edge point only when
        /// <c>Project</c> says so — the tests below shape it with a predicate.</summary>
        private sealed class BoxNavmesh : INavmeshProvider
        {
            public Func<Vector3, Vector3?> Project = p => p;
            public uint LastMask;
            public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
            {
                LastMask = layerMask;
                var r = Project(position);
                snapped = r ?? position;
                return r.HasValue;
            }
            public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF) => Project(position).HasValue;
            public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF) => 0;
            public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;
            public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => 0f;
            public uint QueryVersion() => 1;
            public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF) => 0;
        }

        private static PlacementRequestBatch Request(float maxShift, params PlacementSlot[] slots)
            => new() { RequestId = 9, SourceNodeId = (NodeId)3, MaxShift = maxShift, Slots = slots.ToList() };

        private static PlacementSlot Slot(int key, float x, float y, PlacementLayer layer = PlacementLayer.Infantry, float radius = 0.5f)
            => new() { Key = key, Point = new Vector3(x, y, 0f), Layer = layer, Radius = radius };

        private PlacementResponseBatch Run(PlacementSolverSystem solver, PlacementRequestBatch req)
        {
            var view = (ISimulationView)_world;
            _world.Bus.PublishManaged(req);
            _world.Bus.SwapBuffers();
            solver.Execute(view, 0f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            return Assert.Single(view.ReadManagedEvents<PlacementResponseBatch>());
        }

        [Fact]
        public void OnTheNavmesh_AWalkablePoint_IsExact_AndTheResponseEchoesTheRequest()
        {
            var nav = new BoxNavmesh();
            var r = Run(new PlacementSolverSystem(nav), Request(30f, Slot(1, 10, 20)));
            Assert.Equal(9, r.RequestId);
            Assert.Equal(3, (int)r.SourceNodeId);
            var a = Assert.Single(r.Answers);
            Assert.Equal(1, a.Key);
            Assert.Equal(PlacementOutcome.Exact, a.Outcome);
            Assert.Equal(new Vector3(10, 20, 0), a.Point);
            Assert.Equal((uint)NavLayerMask.Infantry, nav.LastMask);   // the layer's mask
        }

        [Fact]
        public void TheLayer_PicksTheNavmeshMask()
        {
            var nav = new BoxNavmesh();
            Run(new PlacementSolverSystem(nav), Request(30f, Slot(1, 0, 0, PlacementLayer.Vehicle)));
            Assert.Equal((uint)NavLayerMask.Vehicle, nav.LastMask);
        }

        [Fact]
        public void ASnapWithinMaxShift_IsShifted_AndBeyondIt_Failed()
        {
            var nav = new BoxNavmesh { Project = p => new Vector3(5f, p.Y, p.Z) };   // the only walkable ground is the line x = 5
            var near = Run(new PlacementSolverSystem(nav), Request(10f, Slot(1, 0, 0)));
            var a = Assert.Single(near.Answers);
            Assert.Equal(PlacementOutcome.Shifted, a.Outcome);
            Assert.Equal(5f, a.Point.X, 3);

            // beyond: MaxShift 3 m < the 5 m to the line (every ring candidate within 3 m is still >= 2 m off it)
            var far = Run(new PlacementSolverSystem(nav), Request(3f, Slot(2, 0, 0)));
            var f = Assert.Single(far.Answers);
            Assert.Equal(PlacementOutcome.Failed, f.Outcome);
            Assert.Contains("no walkable ground within 3", f.Reason);
            Assert.Equal(new Vector3(0, 0, 0), f.Point);   // the requested point is echoed on a failure
        }

        [Fact]
        public void NoWalkableGroundAnywhere_Fails_WithTheReason()
        {
            var nav = new BoxNavmesh { Project = _ => null };
            var a = Assert.Single(Run(new PlacementSolverSystem(nav), Request(30f, Slot(1, 0, 0))).Answers);
            Assert.Equal(PlacementOutcome.Failed, a.Outcome);
            Assert.Equal("no walkable ground within 30 m", a.Reason);
        }

        [Fact]
        public void Air_IsAlwaysExact_EvenWhereNothingIsWalkable()
        {
            var nav = new BoxNavmesh { Project = _ => null };
            var a = Assert.Single(Run(new PlacementSolverSystem(nav), Request(0f, Slot(1, 7, 8, PlacementLayer.Air))).Answers);
            Assert.Equal(PlacementOutcome.Exact, a.Outcome);
            Assert.Equal(new Vector3(7, 8, 0), a.Point);
        }

        [Fact]
        public void TwoSlotsAtOnePoint_LandApart_ByAtLeastTheirRadii()
        {
            var r = Run(new PlacementSolverSystem(new BoxNavmesh()), Request(30f, Slot(1, 0, 0, radius: 1.5f), Slot(2, 0, 0, radius: 2f)));
            Assert.Equal(PlacementOutcome.Exact, r.Answers[0].Outcome);
            Assert.Equal(PlacementOutcome.Shifted, r.Answers[1].Outcome);
            float d = Vector2.Distance(new Vector2(r.Answers[0].Point.X, r.Answers[0].Point.Y), new Vector2(r.Answers[1].Point.X, r.Answers[1].Point.Y));
            Assert.True(d >= 3.5f - 1e-3f, $"{d} m apart");
        }

        [Fact]
        public void AnExistingEntitysSpot_IsAvoided()
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new WorldPoint(50, 50, 0) });
            var a = Assert.Single(Run(new PlacementSolverSystem(new BoxNavmesh()), Request(30f, Slot(1, 50, 50, radius: 0.5f))).Answers);
            Assert.Equal(PlacementOutcome.Shifted, a.Outcome);
            float d = Vector2.Distance(new Vector2(a.Point.X, a.Point.Y), new Vector2(50, 50));
            float existingRadius = Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.InteractionRadiusMetres(_world, e);
            Assert.True(d >= 0.5f + existingRadius - 1e-3f, $"{d} m from the existing entity (needs {0.5f + existingRadius})");
        }

        [Fact]
        public void TerrainOnly_ARequestOnWater_IsShiftedToLand_AndAmphibiousStaysPut()
        {
            // a 10 m x 10 m lake around (0,0)
            var lake = new TerrainSurface
            {
                Type = TerrainSurfaceType.Water,
                Polygon = new[] { new Vector2(-5, -5), new Vector2(5, -5), new Vector2(5, 5), new Vector2(-5, 5) },
                Min = new Vector2(-5, -5), Max = new Vector2(5, 5),
            };
            _world.SetSingletonManaged(new TerrainWorld { Surfaces = new[] { lake } });

            var dry = Assert.Single(Run(new PlacementSolverSystem(), Request(30f, Slot(1, 0, 0))).Answers);
            Assert.Equal(PlacementOutcome.Shifted, dry.Outcome);
            Assert.True(Math.Max(Math.Abs(dry.Point.X), Math.Abs(dry.Point.Y)) > 5f, "landed on the water");

            var swim = Assert.Single(Run(new PlacementSolverSystem(), Request(30f, Slot(2, 0, 0, PlacementLayer.Amphibious))).Answers);
            Assert.Equal(PlacementOutcome.Exact, swim.Outcome);

            var tooTight = Assert.Single(Run(new PlacementSolverSystem(), Request(3f, Slot(3, 0, 0))).Answers);
            Assert.Equal(PlacementOutcome.Failed, tooTight.Outcome);
            Assert.Equal("no dry ground within 3 m", tooTight.Reason);
        }

        [Fact]
        public void NoNavmeshAndNoTerrain_IsExact_AndSaysSo()
        {
            var a = Assert.Single(Run(new PlacementSolverSystem(), Request(30f, Slot(1, 4, 5))).Answers);
            Assert.Equal(PlacementOutcome.Exact, a.Outcome);
            Assert.Equal("no terrain", a.Reason);
        }

        /// <summary>⭐ Macros debt A2 (SR-190) — two instances dropped on ONE spot do not overlap: in the same frame (two batches answered in one
        /// tick) and a frame apart (the first's men do not exist yet when the second is asked) — the solver keeps every slot it handed out taken.
        /// 🔴 Red-proof: drop the reservation ⇒ both answers stand on the same point.</summary>
        [Fact]
        public void A2_TwoInstancesDroppedOnOneSpot_DoNotOverlap_InOneFrame_OrTheNext()
        {
            var solver = new PlacementSolverSystem(new BoxNavmesh());
            var first = Assert.Single(Run(solver, Request(30f, Slot(1, 0, 0, radius: 1f))).Answers);
            var second = Assert.Single(Run(solver, Request(30f, Slot(1, 0, 0, radius: 1f))).Answers);   // next frame: the first man is not created yet
            Assert.NotEqual(PlacementOutcome.Failed, second.Outcome);
            Assert.True(Vector2.Distance(new Vector2(first.Point.X, first.Point.Y), new Vector2(second.Point.X, second.Point.Y)) >= 2f - 1e-3f);

            // two batches in ONE frame
            var view = (ISimulationView)_world;
            var fresh = new PlacementSolverSystem(new BoxNavmesh());
            var a = Request(30f, Slot(1, 10, 10, radius: 1f)); a.RequestId = 1;
            var b = Request(30f, Slot(1, 10, 10, radius: 1f)); b.RequestId = 2;
            _world.Bus.PublishManaged(a);
            _world.Bus.PublishManaged(b);
            _world.Bus.SwapBuffers();
            fresh.Execute(view, 0f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            var answers = view.ReadManagedEvents<PlacementResponseBatch>().ToArray();
            Assert.Equal(2, answers.Length);
            var p1 = answers.Single(r => r.RequestId == 1).Answers[0].Point;
            var p2 = answers.Single(r => r.RequestId == 2).Answers[0].Point;
            Assert.True(Vector2.Distance(new Vector2(p1.X, p1.Y), new Vector2(p2.X, p2.Y)) >= 2f - 1e-3f);
        }
    }
}
