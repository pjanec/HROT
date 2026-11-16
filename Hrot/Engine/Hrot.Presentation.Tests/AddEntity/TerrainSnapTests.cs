using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Gizmos;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ CE-5009 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-6, §6 S-4 build notes; R-5033) — what a terrain line snaps to: a road EDGE at its half-width + the
/// offset, a house WALL, the END of a placed line; priority end &gt; wall &gt; road edge; nothing out of reach; a query allocates nothing. 🔴 Red-proofs
/// (compiling mutations): the offset not added to the half-width ⇒ the road rail red; the wall tested after the road ⇒ the priority rail red.
/// </summary>
public sealed class TerrainSnapTests
{
    // a road along y = 0 (half-width 3 ⇒ edges at y = ±3, + the 1 m offset ⇒ ±4) and a house 40..50 × 10..20
    private static readonly RoadCorridor[] Road = { new(new[] { new Vector2(0, 0), new Vector2(100, 0) }, 3f, 0f, 0f) };
    private static TerrainWorld House() => TerrainWorldParser.Parse("""
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{"kind":"building","height":6,"floors":2},
           "geometry":{"type":"Polygon","coordinates":[[[40,10],[50,10],[50,20],[40,20],[40,10]]]}}]}
        """);

    private static TerrainSnap Ready()
    {
        var s = new TerrainSnap { RoadOffset = 1f };
        s.Prepare(House2, Road);
        return s;
    }

    [Fact]
    public void ALine_SnapsToTheRoadEdge_AtItsHalfWidthPlusTheOffset()
    {
        var hit = Ready().Snap(new Vector2(20f, 5.5f), 3f);
        Assert.Equal(SnapKind.RoadEdge, hit.Kind);
        Assert.Equal(20f, hit.Point.X, 3);
        Assert.Equal(4f, hit.Point.Y, 3);
    }

    [Fact]
    public void AWall_WinsOverARoadEdge_InReach_AndNothingFarAway()
    {
        var s = Ready();
        var hit = s.Snap(new Vector2(45f, 7f), 3.1f);   // the wall y = 10 and the edge y = 4 both 3 m away
        Assert.Equal(SnapKind.Wall, hit.Kind);
        Assert.Equal(10f, hit.Point.Y, 3);
        Assert.False(s.Snap(new Vector2(70f, 40f), 3f).Hit);
    }

    [Fact]
    public void TheEndOfAPlacedHedge_WinsOverEverything_AndAQueryAllocatesNothing()
    {
        using var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<TerrainFeature>(); w.RegisterManagedComponent<EditablePolyline>();
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(42f, 6f, 0f), Rotation = Quaternion.Identity });
        w.AddComponent(e, new TerrainFeature { Kind = TerrainFeature.Hedge });
        w.SetManagedComponent(e, new EditablePolyline { Points = new[] { new Vector2(0, 0), new Vector2(-30, 0) } });
        var s = Ready();
        s.PrepareEnds(w);
        var hit = s.Snap(new Vector2(43f, 7f), 3f);   // near the hedge's end (42, 6), a wall and a road edge
        Assert.Equal(SnapKind.LineEnd, hit.Kind);
        Assert.Equal(new Vector2(42f, 6f), hit.Point);

        for (int i = 0; i < 10; i++) { s.PrepareEnds(w); s.Snap(new Vector2(20f + i, 5f), 3f); }   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) { s.Prepare(House2, Road); s.PrepareEnds(w); s.Snap(new Vector2(i * 0.5f, 4.6f), 3f); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static readonly TerrainWorld House2 = House();

    /// <summary>
    /// ⭐ CE-5009 (TE-6) — the panel's options decide: Snap OFF ⇒ nothing snaps (the click lands raw); the road OFFSET of 3 m moves the road-edge snap to HalfWidth + 3 (y = 6, no longer 4) and the index follows the next change. 🔴 Red-proof: <c>SnapWith</c> ignoring <c>Snap</c> ⇒ the off assert
    /// fails; not copying <c>RoadOffset</c> ⇒ the y = 6 assert fails.
    /// </summary>
    [Fact]
    public void TE6_ThePanelsSnapToggleAndRoadOffset_DecideWhatASnapIs()
    {
        var s = new TerrainSnap(); var house = House();
        var o = TerrainDrawOptions.From("feature:2::Line")!;
        Assert.True(o.Snap); Assert.Equal(1f, o.RoadOffset);                                    // on by default, 1 m
        Assert.Equal(4f, s.SnapWith(o, house, Road, null, new Vector2(20f, 5.5f), 3f).Point.Y, 3);

        o.RoadOffset = 3f;
        var hit = s.SnapWith(o, house, Road, null, new Vector2(20f, 7.5f), 3f);
        Assert.Equal(SnapKind.RoadEdge, hit.Kind); Assert.Equal(6f, hit.Point.Y, 3);          // HalfWidth 3 + the offset 3
        Assert.False(s.SnapWith(o, house, Road, null, new Vector2(20f, 4f), 0.5f).Hit);          // the old 4 m edge is gone

        o.Snap = false;
        Assert.False(s.SnapWith(o, house, Road, null, new Vector2(20f, 6f), 3f).Hit);            // off: nothing, even exactly on the edge
        Assert.False(s.SnapWith(o, house, Road, null, new Vector2(45f, 10.2f), 3f).Hit);         // …walls too
    }

    /// <summary>
    /// ⭐ CE-5009 — the hint line: a road edge says its offset ("snapped: road edge +1 m · Ctrl: free", +3 m after the panel changes it); the text is built ONLY when the kind or the offset changes (the same kind again is no change, the very same string), going back to no snap
    /// asks the host to restore the tool's own hint; a steady frame allocates nothing. 🔴 Red-proof: the tracker reporting a change every call ⇒ the no-change assert fails (and bytes).
    /// </summary>
    [Fact]
    public void TE6_TheHint_SaysTheOffset_ChangesOnlyOnAChange_AndAllocatesNothingWhenSteady()
    {
        var t = new TerrainSnap.HintTracker();
        Assert.False(t.Update(SnapKind.None, 1f, out _));                                       // nothing to say yet
        Assert.True(t.Update(SnapKind.RoadEdge, 1f, out var road));
        Assert.Equal("snapped: road edge +1 m · Ctrl: free", road);
        Assert.False(t.Update(SnapKind.RoadEdge, 1f, out _));                                   // the same: no change
        Assert.True(t.Update(SnapKind.RoadEdge, 3f, out var road3)); Assert.Equal("snapped: road edge +3 m · Ctrl: free", road3);
        Assert.True(t.Update(SnapKind.Wall, 3f, out var wall)); Assert.Equal("snapped: a house wall · Ctrl: free", wall);
        Assert.False(t.Update(SnapKind.Wall, 5f, out _));                                       // a wall does not care about the offset
        Assert.True(t.Update(SnapKind.None, 3f, out var none)); Assert.Null(none);              // back to free: the tool's own hint returns

        t.Update(SnapKind.RoadEdge, 1f, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) t.Update(SnapKind.RoadEdge, 1f, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
