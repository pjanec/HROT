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
}
