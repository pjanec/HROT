using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ AQ93 §12.4 — the SUMO road layer on the 2-D map is a SYMBOL that fits its budget and is never drawn in 3-D.
///
/// <para>📌 Measured 2026-10-17, editor live on demo-city: the layer drew the 3-D meshes flat (~51.8k triangles a frame) into the
/// map's 4096-primitive buffer; roads, lights, cars and every entity gizmo were dropped and the map showed nothing (CE-4019).
/// 🔒 User: <i>"symbolic is more than enough (lane centerlines, traffic light as single colored dot). The triangle one is for 3d
/// render"</i> · <i>"no 2d road net gizmo should be shown in 3d map"</i>.</para>
/// </summary>
public sealed class TheRoadLayerFitsTheMapBufferTests
{
    [Fact]
    public void DemoCity_IsDrawnAsLaneCentrelinesAndLightDots_WithinTheBudget_AndNeverIn3D()
    {
        using var world = LoadedWorld();
        var net = TrafficNetCache.Of(world)!;
        world.SetSingletonManaged(new TrafficFrame(Array.Empty<TrafficCarPose>(), _ => false,
            net.Roads.Signals.Select(s => s.LaneHandle).Distinct().ToDictionary(h => h, _ => (byte)'G')));

        var buffer = new DebugPrimitiveBuffer(16384);
        new TrafficRoadGizmo().Draw(world, buffer);
        var frame = buffer.GetFrame().ToArray();

        Assert.Equal(TrafficPlanDetail.Lanes, net.Roads.Plan.Detail);
        Assert.Equal(0, buffer.DroppedCount);
        Assert.True(frame.Length > 500, $"the layer drew {frame.Length} primitives — the roads are missing, the rail proves nothing");
        Assert.True(frame.Length <= TrafficRoadPlan.Budget, $"the road layer drew {frame.Length} primitives; its budget is {TrafficRoadPlan.Budget}");
        Assert.DoesNotContain(frame, p => p.Shape == DebugPrimitiveShape.FilledTriangle);   // no surfaces in 2-D
        Assert.All(frame, p => Assert.True((p.TargetView & PipelineTarget.FlatOnly) != 0, $"a {p.Shape} would be drawn in 3-D too"));
        Assert.Equal(net.Roads.Plan.Lines.Count, frame.Count(p => p.Shape == DebugPrimitiveShape.Line));
        // ⭐ CE-1203 (§5e FL-3) — the Traffic row (was the Entities bit)
        Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(d => new TrafficRoadGizmo().Draw(world, d), DebugTraceLayers.Traffic);
    }

    /// <summary>Every segment of every normal vehicle lane, and nothing else (no sidewalk, no junction interior).</summary>
    [Fact]
    public void TheLanes_AreEveryVehicleLaneSegment()
    {
        var net = TrafficRoadSceneTests.Load();
        int segments = net.Model.LanesByHandle.Where(l => !l.EdgeId.StartsWith(':') && l.AllowsRoadVehicle).Sum(l => Math.Max(0, l.Shape.Count - 1));

        Assert.Equal(segments, net.Roads.Plan.Lines.Count);
    }

    /// <summary>🔒 <i>"some simplification might be necessary - maybe even like not rendering larger road net in 2d map at all"</i>:
    /// lanes → one line per edge → nothing, by the budget.</summary>
    [Fact]
    public void ALargerNet_FallsBackToEdges_ThenToNothing()
    {
        var net = TrafficRoadSceneTests.Load();
        int lights = net.Roads.Signals.Count;
        int lanes = TrafficRoadPlan.LaneCentrelines(net.Model, net.Placement).Count;
        int edges = TrafficRoadPlan.EdgeCentrelines(net.Model, net.Placement).Count;
        Assert.True(edges < lanes, $"edges {edges}, lanes {lanes}");

        var byEdges = TrafficRoadPlan.For(net.Model, net.Placement, lights, budget: edges + lights);
        Assert.Equal(TrafficPlanDetail.Edges, byEdges.Detail);
        Assert.Equal(edges, byEdges.Lines.Count);
        Assert.True(byEdges.DrawsLights);

        var none = TrafficRoadPlan.For(net.Model, net.Placement, lights, budget: edges + lights - 1);
        Assert.Equal(TrafficPlanDetail.None, none.Detail);
        Assert.Empty(none.Lines);
        Assert.False(none.DrawsLights);
    }

    // ── ⭐ CE-4388 — the roads follow the map's view (docs/DESIGN_Map_View_Filter.md §7) ───────────────────────────────────────

    private sealed class FakeView : IGizmoViewFilter
    {
        public bool Active { get; set; } = true;
        public bool Is3D { get; set; }
        public float MetresPerPixel { get; set; }
        public System.Numerics.Vector2 Min, Max;
        public bool InView(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity entity) => true;
        public bool Overlaps(System.Numerics.Vector2 min, System.Numerics.Vector2 max)
            => !Active || Is3D || (max.X >= Min.X && min.X <= Max.X && max.Y >= Min.Y && min.Y <= Max.Y);
    }

    private static (DebugPrimitiveBuffer Buffer, int Lines) DrawWith(EntityRepository world, FakeView view)
    {
        var buffer = new DebugPrimitiveBuffer(16384);
        GizmoViews.Register(buffer, view);
        new TrafficRoadGizmo().Draw(world, buffer);
        return (buffer, buffer.GetFrame().ToArray().Count(p => p.Shape == DebugPrimitiveShape.Line));
    }

    /// <summary>⭐ CE-4388 — with the map's view: at a coarse scale the edge centrelines simplified to about two pixels; close in only the lanes that
    /// overlap the view; in 3-D nothing (a 2-D-only layer). 📐 Measured on real-1: a net over the budget drew NO road in 2-D before. 🔴 Red-proof
    /// (compiles): ignore the view (the <c>viewed</c> branch off) ⇒ the close-in frame draws every lane.</summary>
    [Fact]
    public void TheRoads_FollowTheMapsView_SimplifiedFarOut_OnlyInViewCloseIn_NoneIn3D()
    {
        using var world = LoadedWorld();
        var plan = TrafficNetCache.Of(world)!.Roads.Plan;
        Assert.True(plan.HasPolylines, "the scene's plan carries the polylines");
        var all = new FakeView { MetresPerPixel = 2.5f, Min = new(-1e6f), Max = new(1e6f) };

        var (_, far) = DrawWith(world, all);
        Assert.Equal(Math.Min(TrafficRoadPlan.Budget, plan.EdgeLevelFor(TrafficRoadGizmo.TolerancePixels * 2.5f).Segments), far);
        Assert.True(far > 0 && far < plan.LanePolylines.Segments, $"far out: {far} simplified edge segments (lanes {plan.LanePolylines.Segments})");

        var first = plan.LanePolylines.Lines[0];
        var close = new FakeView { MetresPerPixel = 0.1f, Min = first.Min - new System.Numerics.Vector2(5f), Max = first.Min + new System.Numerics.Vector2(5f) };
        var (_, near) = DrawWith(world, close);
        Assert.True(near > 0, "close in, the lanes in view are drawn");
        Assert.True(near < plan.LanePolylines.Segments, $"close in only the lanes in view: {near} of {plan.LanePolylines.Segments}");

        var (buffer3D, in3D) = DrawWith(world, new FakeView { Is3D = true, MetresPerPixel = 0.3f });
        Assert.Equal(0, in3D);
        Assert.Equal(0, buffer3D.GetFrame().Length);
    }

    /// <summary>⭐ CE-4388 — a plan over the whole-net budget (the earlier "nothing") still keeps every road's polylines, so the view draws them.</summary>
    [Fact]
    public void APlanOverTheBudget_StillDrawsTheRoadsInView()
    {
        var net = TrafficRoadSceneTests.Load();
        var none = TrafficRoadPlan.For(net.Model, net.Placement, net.Roads.Signals.Count, budget: 1);
        Assert.Equal(TrafficPlanDetail.None, none.Detail);
        Assert.True(none.HasPolylines);
        Assert.True(none.EdgeLevelFor(5f).Segments > 0);
        Assert.True(none.EdgeLevelFor(5f).Segments <= none.EdgeLevelFor(0f).Segments);
    }

    private static EntityRepository LoadedWorld()
    {
        var world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TrafficRoadSceneTests.TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        return world;
    }
}
