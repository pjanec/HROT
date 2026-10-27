using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Map;
using Hrot.Traffic;
using Hrot.UI.Common.Map2D;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐⭐ <c>CE-1227</c> (docs/DESIGN_Terrain_World.md §10, R-1013) — the 2-D terrain is a retained layer and takes no slot in the map's
/// gizmo buffer. 🔒 User, 2026-10-20: <i>"When i load scenario uc convoy hit, on that terrain in 2d the entity symbols are not shown and
/// neither the selecgion maraquee … What limits number of gizmos? The terrain has msny houses."</i>
/// </summary>
public sealed class TerrainLayer2DTests
{
    internal static EntityRepository DemoCity()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Hrot", "Subsystems"))) dir = Path.GetDirectoryName(dir)!;
        var root = Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain");
        var world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { root }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        return world;
    }

    /// <summary>
    /// 🔴 The defect, through the PRODUCTION composition: one frame of every map projector over demo-city, into the editor's default
    /// buffer. ⭐ Nothing is dropped and no terrain primitive is in the frame. 📐 Before: the terrain projector alone emitted 73 188
    /// primitives into 16 384 slots. Red-proof: put <c>[GizmoProjector]</c> and <c>IGlobalStatelessGizmo</c> back on
    /// <see cref="Hrot.ScenarioEditor.Gizmos.TerrainPlan2D"/> ⇒ the frame overflows.
    /// </summary>
    [Fact]
    public void CE1227_DemoCity_TheMapFrame_DropsNothing_AndHoldsNoTerrain()
    {
        using var world = DemoCity();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<NetworkIdentity>();
        var plan = TerrainLayer2D.PlanOf(world.GetSingletonManaged<TerrainWorld>());
        Assert.True(plan.FillTriangles + plan.Strokes.Length > MapInteractionContext.DefaultBufferCapacity,
            $"anti-vacuity: demo-city's plan ({plan.FillTriangles} + {plan.Strokes.Length}) must exceed the buffer it used to fill");

        var mi = MapInteractionPack.Build(new MapInteractionContext { World = world, StartEnabled = true });
        mi.StatelessSystem.Execute(world, 0.016f);
        var frame = mi.Buffer.GetFrame().ToArray();

        Assert.True(mi.Buffer.DroppedCount == 0, $"dropped {mi.Buffer.DroppedCount}, frame {frame.Length}: "
            + string.Join(", ", frame.GroupBy(p => (p.DebugLayer, p.Shape)).OrderByDescending(g => g.Count()).Take(8).Select(g => $"L{g.Key.DebugLayer}/{g.Key.Shape}={g.Count()}")));
        Assert.DoesNotContain(frame, p => p.Shape == DebugPrimitiveShape.FilledTriangle);
        Assert.DoesNotContain(frame, p => p.DebugLayer == DebugTraceLayers.Terrain2D);
    }

    /// <summary>⭐ The look moved, not changed: the plan holds the fills, the outlines and the labels, all on the Terrain (2-D) layer.</summary>
    [Fact]
    public void CE1227_ThePlan_HoldsFillsOutlinesAndLabels_OnTheTerrainLayer()
    {
        using var world = DemoCity();
        var plan = TerrainLayer2D.PlanOf(world.GetSingletonManaged<TerrainWorld>());

        Assert.True(plan.FillTriangles > 1000, $"fills {plan.FillTriangles}");
        Assert.Equal(plan.FillTriangles * 3, plan.FillVertices.Length);
        Assert.Contains(plan.Strokes, p => p.Shape == DebugPrimitiveShape.Line);
        Assert.Contains(plan.Strokes, p => p.Shape == DebugPrimitiveShape.Text);
        Assert.All(plan.Strokes, p => Assert.Equal(DebugTraceLayers.Terrain2D, p.DebugLayer));
        Assert.DoesNotContain(plan.Strokes, p => p.Shape == DebugPrimitiveShape.FilledTriangle);
    }

    /// <summary>⭐ Culling: what the view shows, in emission order; an outline shorter than a pixel and labels below
    /// <see cref="TerrainLayer2D.LabelMinZoom"/> are left out.</summary>
    [Fact]
    public void CE1227_Cull_KeepsWhatTheViewShows()
    {
        var ink = new Rgba32(30, 30, 30);
        var strokes = new[]
        {
            DebugPrimitive.MakeLine(new Vector3(10, 10, 0), new Vector3(20, 10, 0), ink, layer: DebugTraceLayers.Terrain2D),     // in view
            DebugPrimitive.MakeLine(new Vector3(900, 10, 0), new Vector3(910, 10, 0), ink, layer: DebugTraceLayers.Terrain2D),   // far east
            DebugPrimitive.MakeLine(new Vector3(30, 30, 0), new Vector3(30.1f, 30, 0), ink, layer: DebugTraceLayers.Terrain2D), // 0.1 m
            DebugPrimitive.MakeText(40, 40, new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("6m 2F"), ink, layer: DebugTraceLayers.Terrain2D),               // a label
        };
        var plan = new TerrainLayer2D.Plan(Array.Empty<Vector2>(), Array.Empty<Rgba32>(), strokes);
        var into = new DebugPrimitive[strokes.Length];
        var view = (new Vector2(0, 0), new Vector2(100, 100));

        // zoom 4: 0.1 m is 0.4 px (left out), the label shows
        int close = TerrainLayer2D.Cull(plan, view, 4f, into);
        Assert.Equal(2, close);
        Assert.Equal(DebugPrimitiveShape.Line, into[0].Shape);
        Assert.Equal(10f, into[0].LineStart.X);
        Assert.Equal(DebugPrimitiveShape.Text, into[1].Shape);

        // zoom 1: below LabelMinZoom — only the in-view outline
        int far = TerrainLayer2D.Cull(plan, view, 1f, into);
        Assert.Equal(1, far);
        Assert.Equal(DebugPrimitiveShape.Line, into[0].Shape);

        // zoom 40: the 0.1 m piece is 4 px now — drawn
        Assert.Equal(3, TerrainLayer2D.Cull(plan, view, 40f, into));
    }

    /// <summary>
    /// ⭐ <c>CE-1420</c> (docs/DESIGN_Director_Timeline_Editor.md §11t "CE-1381 sample") — the 2-D open ground is ONE colour: the 3-D ground colour darkened to a share of its brightness (one ground colour for both maps). 🔴 Mutations: the colour not derived from <c>TerrainLayer3D.GroundColor</c>
    /// ⇒ the colour assertion.
    /// </summary>
    [Fact]
    public void CE1420_TheGroundFill_IsThe3DGroundColour_Darkened()
    {
        var g3 = Fdp.Toolkit.Vis3D.TerrainLayer3D.GroundColor;
        var muted = Hrot.ScenarioEditor.Gizmos.TerrainPlan2D.MutedGround(new Rgba32(g3.R, g3.G, g3.B, 255), 0.35f);
        Assert.Equal((39, 50, 30, 255), ((int)muted.R, (int)muted.G, (int)muted.B, (int)muted.A));   // (112,142,86) × 0.35
        var light = Hrot.ScenarioEditor.Gizmos.TerrainPlan2D.MutedGround(new Rgba32(g3.R, g3.G, g3.B, 255), 0.55f);
        Assert.True(light.G > muted.G && light.G < g3.G, "a lighter share lies between the muted colour and the 3-D ground");
    }
}
