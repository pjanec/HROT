using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐⭐ CE-5011 A + B (docs/DESIGN_Terrain_Editor_UX.md §5 TE-9 / TE-10 / TE-18; mockup panel F) — a placed terrain FEATURE on the map and its drawing ghost. 📐 MEASURED first (<c>2026-11-16</c>, with the Terrain strip OPEN, before this gizmo): a placed point feature (a tree) AND a line
/// feature (a wall) each showed ONE generic entity symbol — <c>SpatialAnchor</c> + pick box + <c>SemanticShape</c> of TKB 8818 — at the entity's position: no handle, no outline, a wall's whole length unmarked and unpickable. Now <see cref="TerrainFeatureGizmo"/> draws a dot / an outline only while the toolbar
/// is open, and the entity symbol draws nothing for a feature. The ghost: a thick line / a dot per tree / the area tinted while drawing, and the shape stays until the live bake lists its guid.
/// </summary>
public sealed class TerrainFeatureGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private static readonly Guid GuidA = Guid.Parse("a2000000-0000-4000-8000-000000000002");

    public TerrainFeatureGizmoTests()
    {
        _w.RegisterComponent<SimTransform>(); _w.RegisterComponent<NetworkIdentity>(); _w.RegisterComponent<TerrainFeature>(); _w.RegisterComponent<SelectionState>();
        _w.RegisterComponent<EntityGuid>(); _w.RegisterManagedComponent<EditablePolyline>();
    }

    public void Dispose() => _w.Dispose();

    private Entity Feature(byte kind, Vector2 at, int net, bool selected = false, string template = "", params Vector2[] relative)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity });
        _w.AddComponent(e, new NetworkIdentity(net));
        _w.AddComponent(e, new TerrainFeature { Kind = kind, Template = new Fdp.Core.FixedString32(template) });
        _w.AddComponent(e, new SelectionState { IsSelected = selected });
        _w.AddComponent(e, new EntityGuid(GuidA));
        if (relative.Length > 0) _w.SetManagedComponent(e, new EditablePolyline { Points = relative, Version = 1 });
        return e;
    }

    private static DebugPrimitive[] All(DebugPrimitiveBuffer b) => b.GetFrame().ToArray();
    private static DebugPrimitive[] Lines(DebugPrimitiveBuffer b) => All(b).Where(p => p.Shape == DebugPrimitiveShape.Line && !DebugPrimitiveBuffer.IsPickTarget(p)).ToArray();
    private static DebugPrimitive[] Picks(DebugPrimitiveBuffer b) => All(b).Where(p => DebugPrimitiveBuffer.IsPickTarget(p)).ToArray();

    /// <summary>
    /// CLOSED (the Terrain toolbar not open) ⇒ no primitive at all, for a point and a line feature. OPEN ⇒ the point is a handle dot (a filled sphere in its family colour + the white centre) with ONE pick box; the line is its outline (one segment per edge, in the kind's colour, DASHED) with the
    /// pick segments of the WHOLE feature. 🔴 Red-proof: the gizmo not asking <c>TerrainLock.IsOpen</c> ⇒ the closed assert fails.
    /// </summary>
    [Fact]
    public void TheGizmo_DrawsNothingClosed_ADotForAPoint_AnOutlineForALine_WhenOpen()
    {
        var tree = Feature(TerrainFeature.Tree, new Vector2(50, 50), 41);
        var wall = Feature(TerrainFeature.Wall, new Vector2(100, 100), 42, relative: new[] { new Vector2(0, 0), new Vector2(60, 0), new Vector2(60, 40) });
        var gizmo = new TerrainFeatureGizmo();
        var closed = new DebugPrimitiveBuffer();
        gizmo.Draw(_w, tree, closed); gizmo.Draw(_w, wall, closed);
        Assert.Empty(All(closed));

        TerrainLock.SetOpen(_w, true);
        var dot = new DebugPrimitiveBuffer(); gizmo.Draw(_w, tree, dot);
        var spheres = All(dot).Where(p => p.Shape == DebugPrimitiveShape.Sphere).ToArray();
        Assert.Equal(2, spheres.Length);                                              // the dot and its white centre
        Assert.Equal(TerrainFeatureLook.Tree, spheres[0].FillColor); Assert.Equal(new Rgba32(255, 255, 255, 255), spheres[1].FillColor);
        Assert.Equal(41, (int)Assert.Single(Picks(dot)).BoxAnchorId);                  // ONE pick box: the dot's, carrying the feature's id

        var outline = new DebugPrimitiveBuffer(); gizmo.Draw(_w, wall, outline);
        var lines = Lines(outline);
        Assert.Equal(2, lines.Length);                                                 // a line: n − 1 edges, open
        Assert.All(lines, l => { Assert.Equal(LineStyle.Dashed, l.LineStyle); Assert.Equal(TerrainFeatureLook.Wall, l.Color); });
        Assert.Equal(new Vector2(100, 100), new Vector2(lines[0].LineStart.X, lines[0].LineStart.Y));   // world points = position + relative
        var picks = Picks(outline);
        Assert.Equal(2, picks.Length); Assert.All(picks, p => Assert.Equal(42, (int)p.BoxAnchorId));     // the whole feature is pickable as ONE (every segment carries its id)

        var area = Feature(TerrainFeature.Wood, Vector2.Zero, 43, relative: new[] { new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 30) });
        var woods = new DebugPrimitiveBuffer(); gizmo.Draw(_w, area, woods);
        Assert.Equal(3, Lines(woods).Length);                                          // an AREA closes
    }

    /// <summary>SELECTED ⇒ the outline is solid blue and thicker; the dot gets a blue ring. 🔴 Red-proof: ignore <c>IsSelected</c> ⇒ stays dashed ⇒ red.</summary>
    [Fact]
    public void ASelectedFeature_IsSolidBlue()
    {
        TerrainLock.SetOpen(_w, true);
        var wall = Feature(TerrainFeature.Wall, new Vector2(100, 100), 42, selected: true, relative: new[] { new Vector2(0, 0), new Vector2(60, 0) });
        var tree = Feature(TerrainFeature.Tree, new Vector2(50, 50), 41, selected: true);
        var gizmo = new TerrainFeatureGizmo();
        var b = new DebugPrimitiveBuffer(); gizmo.Draw(_w, wall, b);
        Assert.All(Lines(b), l => { Assert.Equal(LineStyle.Solid, l.LineStyle); Assert.Equal(TerrainFeatureGizmo.SelectedColour, l.Color); });
        var d = new DebugPrimitiveBuffer(); gizmo.Draw(_w, tree, d);
        Assert.Equal(TerrainFeatureGizmo.SelectedColour, All(d).First(p => p.Shape == DebugPrimitiveShape.Sphere).Color);
    }

    /// <summary>A steady frame allocates nothing, for a dot and an outline. 🔴 Red-proof: rebuild the world points every frame ⇒ bytes ⇒ red.</summary>
    [Fact]
    public void ASteadyFrame_AllocatesNothing()
    {
        TerrainLock.SetOpen(_w, true);
        var wall = Feature(TerrainFeature.Wall, new Vector2(100, 100), 42, relative: new[] { new Vector2(0, 0), new Vector2(60, 0), new Vector2(60, 40) });
        var tree = Feature(TerrainFeature.Tree, new Vector2(50, 50), 41);
        var gizmo = new TerrainFeatureGizmo(); var buffer = new DebugPrimitiveBuffer();
        for (int k = 0; k < 3; k++) { buffer.Clear(); gizmo.Draw(_w, wall, buffer); gizmo.Draw(_w, tree, buffer); }
        buffer.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        gizmo.Draw(_w, wall, buffer); gizmo.Draw(_w, tree, buffer);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>One mark, not two: the generic entity symbol draws NOTHING for a placed feature (open or closed), and still draws for an ordinary entity. 🔴 Red-proof: remove the feature test in <c>EntityPresentationGizmo.Draw</c> ⇒ a symbol appears ⇒ red.</summary>
    [Fact]
    public void TheEntitySymbol_DrawsNothingForAFeature_ButForAnOrdinaryEntity()
    {
        var wall = Feature(TerrainFeature.Wall, new Vector2(100, 100), 42, relative: new[] { new Vector2(0, 0), new Vector2(60, 0) });
        var plain = _w.CreateEntity();
        _w.AddComponent(plain, new SimTransform { Position = new Vector3(5, 5, 0), Rotation = Quaternion.Identity }); _w.AddComponent(plain, new NetworkIdentity(60));
        foreach (bool open in new[] { false, true })
        {
            TerrainLock.SetOpen(_w, open);
            var b = new DebugPrimitiveBuffer(); new EntityPresentationGizmo().Draw(_w, wall, b);
            Assert.Empty(All(b));
        }
        var p = new DebugPrimitiveBuffer(); new EntityPresentationGizmo().Draw(_w, plain, p);
        Assert.NotEmpty(All(p));
    }

    // ── B — the drawing ghost and stay-until-baked ───────────────────────────────────────────────────────────────

    private static readonly Vector2[] Row124 = { new(100, 250), new(160, 250), new(160, 314.4f) };

    /// <summary>The ghost of a Row (124.4 m at 8 m) is SIXTEEN dots, at the expander's own places; a wall's is a thick line, a group's the area tinted. 🔴 Red-proof: spacing + 1 in the ghost ⇒ 14 dots ⇒ red.</summary>
    [Fact]
    public void TheDrawingGhost_ARowIs16Dots_AWallAThickLine_AGroupATint()
    {
        var ghost = new TerrainDrawGhost(() => _w);
        var row = TerrainDrawOptions.From("feature:14:tree:Line")!;
        var b = new DebugPrimitiveBuffer(); ghost.UnderlayOf(row)(b, Row124);
        Assert.Equal(16, ghost.RowDots.Count);
        Assert.Equal(16, All(b).Count(p => p.Shape == DebugPrimitiveShape.Sphere));
        Assert.Equal(FeatureExpander.CountAlong(Row124, 8f), ghost.RowDots.Count);
        row.Spacing = 5f; b.Clear(); ghost.UnderlayOf(row)(b, Row124);
        Assert.Equal(FeatureExpander.CountAlong(Row124, 5f), ghost.RowDots.Count);       // the dots follow the panel's spacing

        var wall = TerrainDrawOptions.From("feature:2::Line")!;
        var w = new DebugPrimitiveBuffer(); ghost.UnderlayOf(wall)(w, Row124);
        var lines = All(w).Where(p => p.Shape == DebugPrimitiveShape.Line).ToArray();
        Assert.Equal(4, lines.Length);                                                     // per segment: the band and the line
        Assert.Equal(2, lines.Count(l => l.SizeMode == SizeMode.ScreenPixels && l.ThicknessU16 >= 30));   // thick in 2-D at any zoom
        // ⭐ CE-4594 item 7 — and a band in METRES wide enough that the 3-D drawer drapes it as a RIBBON (a pixel width is a hairline there)
        Assert.Equal(2, lines.Count(l => l.SizeMode == SizeMode.WorldMeters && l.ThicknessU16 / 10f >= Fdp.Toolkit.Vis3D.GizmoRenderer3D.RibbonFromMetres));

        var group = TerrainDrawOptions.From("feature:15:tree:Area")!;
        var g = new DebugPrimitiveBuffer(); ghost.UnderlayOf(group)(g, new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 40), new Vector2(0, 40) });
        Assert.Equal(2, All(g).Count(p => p.Shape == DebugPrimitiveShape.FilledTriangle));
    }

    /// <summary>The ghost allocates nothing over steady frames — the dots are rebuilt only when the committed points change. 🔴 Red-proof: recompute <c>LinePlacesSpaced</c> every frame ⇒ bytes ⇒ red.</summary>
    [Fact]
    public void TheDrawingGhost_AllocatesNothingOverSteadyFrames()
    {
        var ghost = new TerrainDrawGhost(() => _w);
        var row = TerrainDrawOptions.From("feature:14:tree:Line")!; var wall = TerrainDrawOptions.From("feature:2::Line")!;
        var group = TerrainDrawOptions.From("feature:15:tree:Area")!;
        var sq = new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 40), new Vector2(0, 40) };
        var b = new DebugPrimitiveBuffer();
        for (int k = 0; k < 3; k++) { b.Clear(); ghost.Draw(b, Row124, row); ghost.Draw(b, Row124, wall); ghost.Draw(b, sq, group); }
        b.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        ghost.Draw(b, Row124, row); ghost.Draw(b, Row124, wall); ghost.Draw(b, sq, group);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>
    /// STAY UNTIL BAKED: a committed wall stays drawn (faint) after the finish — through the frames where its entity appears — and is dropped on the frame the live bake's info lists the feature's guid; a shape the bake never lists is dropped after the 30 s safety.
    /// 🔴 Red-proof: never pruning ⇒ the "dropped" asserts fail; pruning at once ⇒ the "still drawn" assert fails.
    /// </summary>
    [Fact]
    public void ACommittedFeature_StaysDrawnUntilTheBakeTakesIt_OrTheSafetyElapses()
    {
        long now = 1000;
        var ghost = new TerrainDrawGhost(() => _w, () => now);
        var wall = TerrainDrawOptions.From("feature:2::Line")!;
        var cmd = new SpawnEntityCommand { InitialTransform = new SimTransform { Position = new Vector3(100, 100, 0) }, InitialComponents = new List<object> { new EditablePolyline { Points = new[] { new Vector2(0, 0), new Vector2(60, 0) } } } };
        ghost.Committed(wall, cmd);
        int Drawn() { var b = new DebugPrimitiveBuffer(); ghost.Draw(b, Array.Empty<Vector2>(), wall); return All(b).Count(p => p.Shape == DebugPrimitiveShape.Line); }

        Assert.Equal(1, Drawn());                                                     // just committed: no entity yet — still drawn
        Feature(TerrainFeature.Wall, new Vector2(100, 100), 42, relative: new[] { new Vector2(0, 0), new Vector2(60, 0) });
        Assert.Equal(1, Drawn());                                                     // the entity exists, the bake has not taken it — still drawn
        _w.RegisterManagedComponent<LiveTerrainBakeInfo>();
        _w.SetSingletonManaged(LiveTerrainBakeInfo.From(1, new[] { new OverlayPart(GuidA + "/0", 1, new Vector2(100, 100), new Vector2(160, 100)) }, null));
        Assert.Equal(0, Drawn()); Assert.Equal(0, ghost.PendingCount);                // the bake lists its guid: gone

        var far = new SpawnEntityCommand { InitialTransform = new SimTransform { Position = new Vector3(400, 400, 0) }, InitialComponents = cmd.InitialComponents };
        ghost.Committed(wall, far);                                                   // another that the bake never lists (no entity either)
        Assert.Equal(1, Drawn());
        now += TerrainDrawGhost.SafetyMs + 1;
        Assert.Equal(0, Drawn());
    }

    /// <summary>
    /// ⭐⭐ CE-5030 BE-4 part 2 (📄 docs/DESIGN_Terrain_Base_Edits.md §5; mockup "after Remove") — a REMOVE edit, the toolbar open, draws the building it took away: its footprint DASHED (the
    /// merged world's <see cref="TerrainWorld.Removed"/> record — the world no longer has the building), a <c>removed</c> chip, and the whole outline as the edit's pick; the Details facts
    /// read the same record (levelled to its floor, its doors). Closed ⇒ nothing. 🔴 Red-proof: drop <c>Removed</c> from the merge ⇒ the ghost falls back to the dot ⇒ red.
    /// </summary>
    [Fact]
    public void CE5030_ARemoveEdit_DrawsTheRemovedBuildingsGhost_AndItsFacts()
    {
        var @base = TerrainWorldParser.Parse(
            "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[0,0,120,60],\"groundZ\":0},\"features\":[" +
            "{\"type\":\"Feature\",\"properties\":{\"kind\":\"building\",\"label\":\"B\",\"building\":{\"footprint\":[[0,0],[10,0],[10,8],[0,8]],\"storeys\":[{\"height\":3,\"walls\":[{\"from\":[0,0],\"to\":[10,0],\"openings\":[{\"kind\":\"door\",\"at\":5,\"width\":1,\"doorId\":\"front\"}]},{\"from\":[10,0],\"to\":[10,8]},{\"from\":[10,8],\"to\":[0,8]},{\"from\":[0,8],\"to\":[0,0]}]}]}},\"geometry\":{\"type\":\"Point\",\"coordinates\":[20,20]}}]}",
            "range");
        var parts = FeatureExpander.OfKind(TerrainFeature.Edit, "e1", "remove", 0f, 0f, 0u, new Vector2(25f, 24f), Array.Empty<Vector2>(), @base, new TerrainAssets(), Array.Empty<string?>(), target: "B");
        var overlay = TerrainWorldParser.Parse(OverlayExpansion.WorldDocument(@base, parts).ToJsonString(), "range", new TerrainAssets(), @base.Height);
        var merged = TerrainWorldMerge.Merge(@base, overlay);
        var gone = Assert.Single(merged.Removed);
        Assert.Equal("B", gone.Label); Assert.Equal(1, gone.Doors); Assert.Equal(4, gone.Footprint.Length);
        _w.RegisterManagedComponent<TerrainWorld>();
        _w.SetSingletonManaged(merged);

        var edit = Feature(TerrainFeature.Edit, new Vector2(25f, 24f), 77, template: "remove");
        var f = _w.GetComponentRO<TerrainFeature>(edit); f.Target = new Fdp.Core.FixedString64("B"); _w.SetComponent(edit, f);
        var gizmo = new TerrainFeatureGizmo();
        var closed = new DebugPrimitiveBuffer(); gizmo.Draw(_w, edit, closed);
        Assert.Empty(All(closed));

        TerrainLock.SetOpen(_w, true);
        var b = new DebugPrimitiveBuffer(); gizmo.Draw(_w, edit, b);
        var lines = Lines(b);
        Assert.Equal(4, lines.Length);                                                 // the footprint, closed
        Assert.All(lines, l => { Assert.Equal(LineStyle.Dashed, l.LineStyle); Assert.Equal(TerrainFeatureGizmo.RemovedGhost, l.Color); });
        Assert.Contains(All(b), p => p.Shape == DebugPrimitiveShape.Text);              // the "removed" chip
        Assert.DoesNotContain(All(b), p => p.Shape == DebugPrimitiveShape.Sphere);     // not the handle dot
        Assert.All(Picks(b), p => Assert.Equal(77, (int)p.BoxAnchorId));
        Assert.Equal(4, Picks(b).Length);

        var facts = TerrainFeatureOps.FactsOf(_w, edit);
        Assert.Equal("Remove building", facts.Action);
        Assert.Equal("B  (baked)", facts.Building);
        Assert.Equal("levelled to 0.0 m", facts.Ground);
        Assert.Equal("1 door · its damage", facts.GoesWithIt);
    }
}
