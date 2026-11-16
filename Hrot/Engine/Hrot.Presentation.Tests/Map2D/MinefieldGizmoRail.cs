using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Common.Editing;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-4624 MS-U2 (docs/designs/minefields/DESIGN_Minefields.md MR-8, KR-11; the mockup's panel A) — the ONE minefield drawing: the truth (every mine,
/// a struck one starred, a cleared one gone), a side that knows the field in full (the tactical graphic, never a single mine), a side that knows nothing
/// (no outline at all), and 0 B on a steady frame.
/// </summary>
public sealed class MinefieldGizmoRail
{
    private static readonly List<Vector2> Outline = new() { new(0, 0), new(230, -20), new(270, 90), new(150, 140), new(20, 120) };

    private static (EntityRepository W, Entity Field) World(MineType type = MineType.Mixed, float spacing = 14f, ushort knownAtStart = 0, int? seatSide = null)
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<Minefield>(); w.RegisterComponent<MinefieldStrikes>(); w.RegisterComponent<EntityGuid>(); w.RegisterComponent<MinefieldLanes>();
        w.RegisterManagedComponent<EditablePolyline>();
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(1000f, 2000f, 0f), Rotation = Quaternion.Identity });
        var f = new Minefield { Type = type, SpacingM = spacing, OrientationDeg = -5f, KnownAtStartSides = knownAtStart };
        f.SetLane(0, new MinefieldLane(new Vector2(1120, 1960), new Vector2(1150, 2160), 4.5f, (byte)(ForceId.Friend + 1), (ushort)(1 << (int)ForceId.Friend)));
        f.LaneCount = 1;
        w.AddComponent(e, f);
        w.AddComponent(e, new EntityGuid(Guid.Parse("11111111-2222-3333-4444-555555555555")));
        w.SetManagedComponent(e, new EditablePolyline { Points = Outline });
        if (seatSide is { } s) StationMode.Install(w, () => EditingMode.Live, side: () => (ForceId)s);
        return (w, e);
    }

    private static GizmoSettingsRegistry Settings(bool sideLimited)
    {
        var r = new GizmoSettingsRegistry();
        var g = new MinefieldGizmo(r);
        r.Write(GizmoSettingsRegistry.ComputeHash(MinefieldGizmo.SideLimitedMap), GizmoSettingValue.From(sideLimited));
        return r;
    }

    private static int Lines(DebugPrimitiveBuffer b, Rgba32 colour)
    {
        int n = 0; var f = b.GetFrame();
        for (int i = 0; i < f.Length; i++) if (f[i].Shape == DebugPrimitiveShape.Line && f[i].Color.Equals(colour)) n++;
        return n;
    }

    private static int Triangles(DebugPrimitiveBuffer b)
    {
        int n = 0; var f = b.GetFrame();
        for (int i = 0; i < f.Length; i++) if (f[i].Shape == DebugPrimitiveShape.FilledTriangle) n++;
        return n;
    }

    [Fact]
    public void TheTruth_DrawsEveryDerivedMine_AStruckOneStarred_AClearedOneGone_TheOutlineAndTheLane()
    {
        var (w, field) = World();
        var gizmo = new MinefieldGizmo(Settings(false));
        var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
        gizmo.Draw(w, field, buffer);
        var c = gizmo.CacheOf(w, field, w.GetComponentRO<Minefield>(field))!;
        Assert.True(c.Mines.Count > 100, $"the derived pattern: {c.Mines.Count}");
        int ap = 0, at = 0;
        foreach (var m in c.Mines) if (m.Type == MineType.AntiTank) at++; else ap++;
        Assert.True(ap > 0 && at > 0, "a mixed field has both");
        Assert.Equal(ap * 2 + at * 4, Lines(buffer, MinefieldGizmo.MineColour));   // an AP cross, an AT square — one mark per mine
        Assert.True(Lines(buffer, MinefieldGizmo.FieldColour) >= Outline.Count, "the outline");
        Assert.Equal(2 + 4, Lines(buffer, MinefieldGizmo.LaneColour));               // two edges + two chevrons
        Assert.Equal(0, Lines(buffer, MinefieldGizmo.StruckColour));

        // one mine goes off, another is cleared: a star for the first, nothing for the second
        var strikes = new MinefieldStrikes { Field = w.GetComponentRO<EntityGuid>(field).Value, Count = 2 };
        strikes.Set(0, c.Mines[0].Row, c.Mines[0].Col, MineGone.Struck);
        strikes.Set(1, c.Mines[1].Row, c.Mines[1].Col, MineGone.Cleared);
        var rec = w.CreateEntity(); w.AddComponent(rec, strikes);
        buffer.Clear();
        gizmo.Draw(w, field, buffer);
        Assert.Equal(4, Lines(buffer, MinefieldGizmo.StruckColour));
        int markOf(Fdp.Toolkit.Terrain.Mine m) => m.Type == MineType.AntiTank ? 4 : 2;
        Assert.Equal(ap * 2 + at * 4 - markOf(c.Mines[0]) - markOf(c.Mines[1]), Lines(buffer, MinefieldGizmo.MineColour));
    }

    [Fact]
    public void ASideThatKnowsTheFieldInFull_SeesTheTacticalGraphicAndItsLane_NeverASingleMine_ASideThatKnowsNothing_SeesNoField()
    {
        // Blue (Friend = 0) knew it at start, the gate is ON, the seat is Blue
        var (w, field) = World(knownAtStart: 1 << (int)ForceId.Friend, seatSide: (int)ForceId.Friend);
        var gizmo = new MinefieldGizmo(Settings(true));
        Assert.Equal((int)ForceId.Friend, gizmo.ViewerSide(w));
        var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
        gizmo.Draw(w, field, buffer);
        Assert.Equal(0, Lines(buffer, MinefieldGizmo.MineColour));
        Assert.True(Triangles(buffer) >= Outline.Count - 2, "the field's light fill");
        Assert.True(Lines(buffer, MinefieldGizmo.FieldColour) > Outline.Count, "outline + edge symbols + signs");
        Assert.Equal(6, Lines(buffer, MinefieldGizmo.LaneColour));   // the lane is known to Blue

        // Red's seat, the same field: Red knows nothing (no record yet) ⇒ no outline, no mines, no lane
        var (w2, field2) = World(knownAtStart: 1 << (int)ForceId.Friend, seatSide: (int)ForceId.Hostile);
        buffer.Clear();
        gizmo.Draw(w2, field2, buffer);
        Assert.Equal(0, Lines(buffer, MinefieldGizmo.FieldColour));
        Assert.Equal(0, Lines(buffer, MinefieldGizmo.MineColour));
        Assert.Equal(0, Lines(buffer, MinefieldGizmo.LaneColour));

        // the gate OFF (KR-11, the default): Red's seat sees the truth
        var truth = new MinefieldGizmo(Settings(false));
        Assert.Null(truth.ViewerSide(w2));
        buffer.Clear();
        truth.Draw(w2, field2, buffer);
        Assert.True(Lines(buffer, MinefieldGizmo.MineColour) > 0);
    }

    [Fact]
    public void TheCache_FollowsTheFieldsParameters_AndASteadyFrameAllocatesNothing()
    {
        var (w, field) = World(type: MineType.AntiPersonnel, spacing: 14f);
        var gizmo = new MinefieldGizmo(Settings(false));
        var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
        int before = gizmo.CacheOf(w, field, w.GetComponentRO<Minefield>(field))!.Mines.Count;
        var f = w.GetComponentRO<Minefield>(field); f.SpacingM = 7f; w.SetComponent(field, f);
        int after = gizmo.CacheOf(w, field, w.GetComponentRO<Minefield>(field))!.Mines.Count;
        Assert.True(after > before * 3, $"halving the spacing ~quadruples the mines: {before} -> {after}");
        Assert.Equal("AP · 7 m", MinefieldGizmo.LabelOf(w.GetComponentRO<Minefield>(field)));

        for (int i = 0; i < 20; i++) { buffer.Clear(); gizmo.Draw(w, field, buffer); }
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) { buffer.Clear(); gizmo.Draw(w, field, buffer); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - bytes);   // ⭐ R-2084: 0 B a steady frame
    }

    [Fact]
    public void AKnowledgeCell_IsDrawnOnTheFieldsRotatedLattice()
    {
        var (w, field) = World();
        var gizmo = new MinefieldGizmo(Settings(false));
        var c = gizmo.CacheOf(w, field, w.GetComponentRO<Minefield>(field))!;
        MinefieldGizmo.CellQuad(c.Grid, 2, 3, out var a, out var b, out var cc, out var d);
        Assert.InRange(Vector2.Distance(a, b), c.Grid.Cell - 0.01f, c.Grid.Cell + 0.01f);
        var centre = (a + cc) / 2f;
        Assert.True(MinefieldKnowledgeOps.CellOf(c.Grid, centre, out int col, out int row));
        Assert.Equal((2, 3), (col, row));
    }

    [Fact]
    public void ARuntimeLane_TheClearerCut_IsDrawnForTheTruth_AndForTheSideThatCutIt_NotForTheOther()
    {
        var (w, field) = World(knownAtStart: 1 << (int)ForceId.Friend, seatSide: (int)ForceId.Hostile);
        var rec = new MinefieldLanes { Field = w.GetComponentRO<EntityGuid>(field).Value };
        Assert.True(rec.TryAdd(new LaneItem(rec.Field, new Vector2(1050, 1990), new Vector2(1060, 2150), 4.5f, (byte)(ForceId.Friend + 1))));
        var r = w.CreateEntity(); w.AddComponent(r, rec);
        var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
        new MinefieldGizmo(Settings(false)).Draw(w, field, buffer);
        Assert.Equal(12, Lines(buffer, MinefieldGizmo.LaneColour));   // the authored lane + the runtime one, 6 lines each
        buffer.Clear();
        new MinefieldGizmo(Settings(true)).Draw(w, field, buffer);    // Red's seat: Blue cut it
        Assert.Equal(0, Lines(buffer, MinefieldGizmo.LaneColour));
        var (w2, field2) = World(seatSide: (int)ForceId.Friend);         // Blue's seat, the field NOT known: only Blue's own runtime lane
        var rec2 = new MinefieldLanes { Field = w2.GetComponentRO<EntityGuid>(field2).Value };
        rec2.TryAdd(new LaneItem(rec2.Field, new Vector2(1050, 1990), new Vector2(1060, 2150), 4.5f, (byte)(ForceId.Friend + 1)));
        var r2 = w2.CreateEntity(); w2.AddComponent(r2, rec2);
        buffer.Clear();
        new MinefieldGizmo(Settings(true)).Draw(w2, field2, buffer);
        Assert.Equal(6, Lines(buffer, MinefieldGizmo.LaneColour));
    }

    /// <summary>
    /// ⭐ CE-4624 (the LIVE check, 2026-11-16) — a placed field is NO UNIT: the generic entity gizmo drew a white 2-D box at its anchor and the 3-D body layer an unknown purple box. 🔴 Red-proof: the
    /// minefield line removed from <c>NotAUnit</c> ⇒ the first assertion; the type removed from <c>EntityBodyLayer3D.LookOf</c> ⇒ the second.
    /// </summary>
    [Fact]
    public void AMinefieldEntity_IsNotAUnit_NoGenericBox_NoUnknownBody()
    {
        var (w, field) = World();
        Assert.True(Hrot.Common.Diagnostics.Gizmos.NotAUnit.Is(w, field));
        Assert.False(Hrot.Common.Diagnostics.Gizmos.Seeable.OnMap(w, field));
        var look = new Hrot.UI.Common.Map3D.EntityBodyLayer3D(() => w, () => null).LookOf(Minefields.TkbType, null);
        Assert.Equal(Hrot.UI.Common.Map3D.VisualFamily.Unit, look.Family);   // the "no body" look, not the unknown purple box
        Assert.Equal(Vector3.Zero, look.Size);
    }
}
