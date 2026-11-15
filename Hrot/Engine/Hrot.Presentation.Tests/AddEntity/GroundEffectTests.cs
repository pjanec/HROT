using System.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐⭐ <c>CE-4592</c> / <c>CE-4593</c> (P-5 part 2b; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §9.16) — the drawn shape's BIRTH and its EFFECT: a drawn Raise is born with its
/// value and the newest draw order (⛔ was: 0 m and a random order), a ditch keeps its preset's depth unless one is typed; the effect a shape has — the grade it gives a road, the
/// houses and roads kept out, the area, the change — measured on the same merge every node runs.
/// </summary>
public sealed class GroundEffectTests
{
    private static TerrainWorld Flat(params TerrainBuilding[] buildings)
    {
        int n = 201;
        return new TerrainWorld { Name = "flat", BoundsMin = new Vector2(-100f), BoundsMax = new Vector2(100f), Height = new TerrainHeightGrid(new Vector2(-100f), 1f, n, n, new float[n * n]), Buildings = buildings };
    }

    private static TerrainRoads Road()
    {
        static global::CarKinem.Road.Vector2Json V(float x, float y) => new() { X = x, Y = y };
        return TerrainRoads.FromJson(new global::CarKinem.Road.RoadNetworkJson
        {
            Nodes = new[] { new global::CarKinem.Road.RoadNodeJson { Id = 0, Position = V(-90f, 0f) }, new global::CarKinem.Road.RoadNodeJson { Id = 1, Position = V(90f, 0f) } },
            Segments = new[] { new global::CarKinem.Road.RoadSegmentJson { Id = 0, StartNodeId = 0, EndNodeId = 1, LaneWidth = 3.5f, LaneCount = 2, ControlPoints = new() { P0 = V(-90f, 0f), T0 = V(180f, 0f), P1 = V(90f, 0f), T1 = V(180f, 0f) } } },
        });
    }

    private static Vector2[] Square(float cx, float cy, float h) => new[] { new Vector2(cx - h, cy - h), new Vector2(cx + h, cy - h), new Vector2(cx + h, cy + h), new Vector2(cx - h, cy + h) };

    /// <summary>
    /// ⭐ A drawn Raise is born with 2 m and a draw order of NOW (the newest wins); Lower with a typed 3 m keeps 3 m; a ditch with no typed depth keeps 0 (= its preset's), an anti-tank
    /// ditch typed 2.5 m keeps it. 🔴 Red-proof: the old <c>Born</c> (Pad = the line height, Seed = a random seed) ⇒ the Raise is born 0 m with a four-digit order.
    /// </summary>
    [Fact]
    public void ADrawnGroundShape_IsBornWithItsValueAndTheNewestOrder_ADitchWithItsPreset()
    {
        TerrainFeature BornOf(TerrainDrawOptions o)
        {
            var comps = new List<object> { new SimTransform { Position = new Vector3(0f, 0f, 0f) } };
            o.Born(comps);
            return Assert.Single(comps, c => c is TerrainFeature) is TerrainFeature f ? f : default;
        }
        uint before = TerrainBirth.DrawOrderNow();
        var raise = TerrainDrawOptions.From("feature:13:raise:Area")!;
        Assert.True(raise.IsGround && raise.HasPanel);
        var r = BornOf(raise);
        Assert.Equal("raise", r.Template.ToString());
        Assert.Equal(TerrainBirth.DefaultRaise, r.Pad);
        Assert.True(r.Seed >= before, "the draw order is the authoring moment — the newest wins");

        var lower = TerrainDrawOptions.From("feature:13:raise:Area")!;
        lower.SetMode("lower"); lower.GroundValue = 3f;
        Assert.Equal("Lower by", lower.ValueLabel);
        var l = BornOf(lower);
        Assert.Equal("lower", l.Template.ToString()); Assert.Equal(3f, l.Pad);

        var ditch = TerrainDrawOptions.From("feature:13:ditch:Line")!;
        Assert.True(ditch.IsDitch && ditch.HasPanel);
        Assert.Equal("Ditch", ditch.KindName); Assert.Equal(1f, ditch.GroundValueShown);   // the preset's depth shown
        Assert.Equal(0f, BornOf(ditch).Pad);                                                // 0 = the preset's
        ditch.SetMode("at-ditch"); ditch.GroundValue = 2.5f;
        var at = BornOf(ditch);
        Assert.Equal("at-ditch", at.Template.ToString()); Assert.Equal(2.5f, at.Pad);
    }

    /// <summary>
    /// ⭐ G-19 / G-18 / G-24 — a 5 m raise across a road: the grade over 15 % (warned), one house and one road kept out, 400 m², a +5 m change and a tint; a ditch across the road says it
    /// stops there (G-29). 🔴 Red-proof: no roads passed ⇒ no corridor, grade 0, no warning.
    /// </summary>
    [Fact]
    public void AShapeOverARoad_ReportsTheGrade_WhatIsKeptOut_AndTheChange_ADitchStopsAtTheRoad()
    {
        var house = new TerrainBuilding { Footprint = Square(5f, 6f, 2f) };
        var world = Flat(house);
        var assets = new TerrainAssets();
        var fx = GroundEffect.Of(world, Road(), assets, "raise", 5f, Square(0f, 5f, 10f));
        Assert.True(fx.OverGrade, $"grade {fx.RoadGrade}");
        Assert.Equal(1, fx.HousesKept); Assert.Equal(1, fx.RoadsKept);
        Assert.Equal(400f, fx.Area, 1);
        Assert.Equal(5f, fx.Change, 2);
        Assert.NotEmpty(fx.CellMin); Assert.Equal(fx.CellMin.Length, fx.CellDelta.Length);
        var (l1, warn, l2) = TerrainDrawPanel.Hints(fx);
        Assert.True(warn); Assert.StartsWith("steepest road grade ", l1); Assert.EndsWith("(over 15 %) — roads follow; SUMO's net after save", l1);
        Assert.Equal("kept out: 1 house, 1 road corridor", l2);

        var none = GroundEffect.Of(world, null, assets, "raise", 5f, Square(0f, 5f, 10f));
        Assert.Equal(0f, none.RoadGrade); Assert.Equal(0, none.RoadsKept);

        var ditch = GroundEffect.Of(world, Road(), assets, "ditch", 0f, new[] { new Vector2(-40f, -30f), new Vector2(-40f, 30f) });
        Assert.True(ditch.IsDitch); Assert.Equal(1, ditch.RoadsKept);
        Assert.Equal("stops at the road — G-29", TerrainDrawPanel.Hints(ditch).Line1);
        Assert.True(ditch.ObstacleParts > 1, "the ditch was cut at the road");
    }

    /// <summary>
    /// ⭐ Mockup B's tint: every changed cell two filled triangles under the shape — amber where the ground rises, blue where it falls, the strongest change the most opaque — and a
    /// steady frame allocates nothing (R-2084); the area gizmo calls its underlay with the placed points. 🔴 Red-proof: drop the <c>Underlay</c> call in <c>UpdateAndDraw</c> ⇒ no triangles.
    /// </summary>
    [Fact]
    public void TheTint_PaintsEveryChangedCell_ByTheDirectionOfTheChange_WithNoAllocation_AndTheAreaGizmoDrawsIt()
    {
        var world = Flat();
        var raise = GroundEffect.Of(world, null, new TerrainAssets(), "raise", 5f, Square(0f, 0f, 10f));
        var draw = new TriDraw();
        Hrot.ScenarioEditor.Gizmos.GroundTintDraw.Draw(draw, raise);
        Assert.Equal(2 * raise.CellMin.Length, draw.Triangles);
        Assert.All(draw.Colours, c => Assert.Equal((Hrot.ScenarioEditor.Gizmos.GroundTintDraw.Raise.R, Hrot.ScenarioEditor.Gizmos.GroundTintDraw.Raise.G), (c.R, c.G)));
        Assert.Contains(draw.Colours, c => c.A == Hrot.ScenarioEditor.Gizmos.GroundTintDraw.MaxAlpha);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Hrot.ScenarioEditor.Gizmos.GroundTintDraw.Draw(draw.Reset(), raise);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        var lower = GroundEffect.Of(world, null, new TerrainAssets(), "lower", 2f, Square(0f, 0f, 10f));
        Hrot.ScenarioEditor.Gizmos.GroundTintDraw.Draw(draw.Reset(), lower);
        Assert.All(draw.Colours, c => Assert.Equal(Hrot.ScenarioEditor.Gizmos.GroundTintDraw.Lower.B, c.B));

        // the area gizmo draws its underlay first, with the points placed so far
        var gizmo = new Fdp.Toolkit.Vis2D.Gizmos.PointSequenceGizmo(_ => { }, () => { }, minPoints: 3, closed: true);
        int seen = -1;
        gizmo.Underlay = (d, pts) => { seen = pts.Count; Hrot.ScenarioEditor.Gizmos.GroundTintDraw.Draw(d, raise); };
        using var repo = new EntityRepository();
        foreach (var p in Square(0f, 0f, 10f)) gizmo.Session.Add(p);
        gizmo.UpdateAndDraw(repo, 0.016f, draw.Reset());
        Assert.Equal(4, seen);
        var amber = Hrot.ScenarioEditor.Gizmos.GroundTintDraw.Raise;
        Assert.Equal(2 * raise.CellMin.Length, draw.Colours.Count(c => c.R == amber.R && c.G == amber.G && c.B == amber.B));   // the tint (the gizmo adds its own fill)
    }

    /// <summary>⭐ G-37 — on a 2 m grid a ditch says it will not form (amber), on a 1 m grid it does not; a ground area never says it. 🔴 Red-proof: drop the <c>WillNotForm</c> line in <c>Hints</c> ⇒ "no road crossed".</summary>
    [Fact]
    public void OnACoarseGrid_ADitchSaysItWillNotForm()
    {
        int n = 101;
        var coarse = new TerrainWorld { Name = "coarse", BoundsMin = new Vector2(-100f), BoundsMax = new Vector2(100f), Height = new TerrainHeightGrid(new Vector2(-100f), 2f, n, n, new float[n * n]) };
        var line = new[] { new Vector2(-20f, 0f), new Vector2(20f, 0f) };
        var fx = GroundEffect.Of(coarse, null, new TerrainAssets(), "ditch", 0f, line);
        Assert.True(fx.WillNotForm);
        var (l1, warn, _) = TerrainDrawPanel.Hints(fx);
        Assert.True(warn); Assert.Equal("the grid is 2 m: it will not form here (G-37)", l1);
        Assert.False(GroundEffect.Of(Flat(), null, new TerrainAssets(), "ditch", 0f, line).WillNotForm);
        Assert.False(GroundEffect.Of(coarse, null, new TerrainAssets(), "raise", 2f, Square(0f, 0f, 10f)).WillNotForm);
    }

    private sealed class TriDraw : Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder
    {
        public int Triangles;
        public readonly List<Fdp.Toolkit.Diagnostics.Gizmos.Rgba32> Colours = new(4096);
        public TriDraw Reset() { Triangles = 0; Colours.Clear(); return this; }
        public void DrawFilledTriangle(Vector2 a, Vector2 b, Vector2 c, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 color, byte layer = 0, Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget target = Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All)
        { Triangles++; if (Colours.Count < Colours.Capacity) Colours.Add(color); }
        public void EmitRaw(in Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive prim) { }
        public void DrawContextMenuBinding(int networkId, string menuJson) { }
        public void DrawLine(Vector3 s, Vector3 e, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 c, float t = 1f, Fdp.Toolkit.Diagnostics.Gizmos.SizeMode m = Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.ScreenPixels,
            Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget tg = Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All, byte l = 0, Fdp.Toolkit.Diagnostics.Gizmos.LineStyle style = Fdp.Toolkit.Diagnostics.Gizmos.LineStyle.Solid) { }
        public void DrawLineGradient(Vector3 s, Vector3 e, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 sc, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 ec, float t = 1f, Fdp.Toolkit.Diagnostics.Gizmos.SizeMode m = Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.ScreenPixels,
            Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget tg = Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All, byte l = 0, Fdp.Toolkit.Diagnostics.Gizmos.LineStyle style = Fdp.Toolkit.Diagnostics.Gizmos.LineStyle.Solid) { }
        public void DrawSphere(Vector3 c, float r, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 col, float thickness = 0f, Fdp.Toolkit.Diagnostics.Gizmos.SizeMode sm2 = Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.WorldMeters,
            Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget tg = Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All, byte l = 0, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 fillColor = default, Fdp.Toolkit.Diagnostics.Gizmos.LineStyle style = Fdp.Toolkit.Diagnostics.Gizmos.LineStyle.Solid) { }
        public void DrawArrow(Vector3 f, Vector3 t, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 c, float h = 1f, byte l = 0) { }
        public void DrawText(float x, float y, Fdp.Core.FixedString32 t, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 c, Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace sp = Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace.World, byte l = 0, float fontSizePx = 0f, float lineOffsetPx = 0f,
            Fdp.Toolkit.Diagnostics.Gizmos.GizmoTextStyle style = Fdp.Toolkit.Diagnostics.Gizmos.GizmoTextStyle.Plain) { }
        public void DrawTextLong(float x, float y, string t, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 c, Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace sp = Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace.World, byte l = 0, float fontSizePx = 0f, float lineOffsetPx = 0f,
            Fdp.Toolkit.Diagnostics.Gizmos.GizmoTextStyle style = Fdp.Toolkit.Diagnostics.Gizmos.GizmoTextStyle.Plain) { }
        public void DrawEntityBadge(Entity e, Fdp.Core.FixedString32 rt, Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget tg = Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All) { }
        public void DrawEntityLocal(int a, Vector3 ls, Vector3 le, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 c, float t = 1f, byte l = 0) { }
        public void DrawEntityLocalInteractive(int a, Vector3 ls, Vector3 le, Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 c, ushort sid, float t = 1f, byte l = 0) { }
    }
}
