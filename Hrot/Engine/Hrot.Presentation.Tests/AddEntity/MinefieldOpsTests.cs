using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ <c>CE-4624</c> MS-U3 (minefield authoring; DESIGN_Minefields MR-8, MR-18, MF-12) — <see cref="MinefieldOps"/>: every edit ONE undo step and a no-op when unchanged, the spacing / orientation ranges, the lanes (add — at most 8 — and delete,
/// compacting), the shape lock and its reason, the derived counts (cached: 0 B steady); the area tool's minefield birth (no generic overlay, a default <see cref="Minefield"/>), the picker entry, and the vertex editor's REFUSAL of an outline
/// change on a locked field (through <see cref="OutlineEditPolicy"/>, never a minefield test in the gizmo). 🔴 Red-proofs: <c>OutlineRefusal</c> returning null ⇒ the vertex rail goes red; <c>DeleteLane</c> not compacting ⇒ the lane rail.
/// </summary>
public sealed class MinefieldOpsTests
{
    private sealed class Recorder : IEditStepRecorder
    {
        public readonly List<string> Log = new();
        public void Begin(EntityRepository repo, string label) => Log.Add("begin " + label);
        public void End(EntityRepository repo) => Log.Add("end");
        public void Cancel(EntityRepository repo) => Log.Add("cancel");
    }

    private static readonly Guid FieldGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly List<Vector2> Outline = new() { new(0, 0), new(230, -20), new(270, 90), new(150, 140), new(20, 120) };

    private static (EntityRepository W, Entity Field, Recorder Rec) World()
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<Minefield>(); w.RegisterComponent<MinefieldStrikes>(); w.RegisterComponent<EntityGuid>();
        w.RegisterManagedComponent<EditablePolyline>();
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(1000f, 2000f, 0f), Rotation = Quaternion.Identity });
        w.AddComponent(e, MinefieldOps.Default());
        w.AddComponent(e, new EntityGuid(FieldGuid));
        w.SetManagedComponent(e, new EditablePolyline { Points = new List<Vector2>(Outline) });
        var rec = new Recorder(); EditSteps.Install(w, rec);
        return (w, e, rec);
    }

    private static void Strike(EntityRepository w, Guid field, MineGone how, short row = 3, short col = 4)
    {
        var s = new MinefieldStrikes { Field = field, Count = 1 };
        s.Set(0, row, col, how);
        var rec = w.CreateEntity(); w.AddComponent(rec, s);
    }

    [Fact]
    public void EveryEdit_IsOneUndoStep_AndNothingChangedIsNoStep()
    {
        var (w, e, rec) = World();
        Assert.True(MinefieldOps.SetType(w, e, MineType.AntiTank));
        Assert.Equal(new[] { "begin Set minefield type", "end" }, rec.Log);
        rec.Log.Clear();
        Assert.False(MinefieldOps.SetType(w, e, MineType.AntiTank));          // unchanged
        Assert.False(MinefieldOps.SetType(w, e, MineType.None));              // not a laid type
        Assert.False(MinefieldOps.SetSpacing(w, e, 10f));
        Assert.False(MinefieldOps.SetOrientation(w, e, 0f));
        Assert.False(MinefieldOps.SetSeed(w, e, 1u));
        Assert.False(MinefieldOps.SetAntiHandling(w, e, false));
        Assert.False(MinefieldOps.SetKnownAtStart(w, e, 1, false));
        Assert.Empty(rec.Log);
        Assert.True(MinefieldOps.SetSpacing(w, e, 14f)); Assert.True(MinefieldOps.SetOrientation(w, e, 85f)); Assert.True(MinefieldOps.SetSeed(w, e, 7u));
        Assert.True(MinefieldOps.SetAntiHandling(w, e, true)); Assert.True(MinefieldOps.SetKnownAtStart(w, e, 1, true)); Assert.True(MinefieldOps.Reroll(w, e));
        Assert.Equal(12, rec.Log.Count);                                       // six edits, each a begin / end pair
        var f = w.GetComponentRO<Minefield>(e);
        Assert.Equal((14f, 85f, (byte)1, (ushort)2), (f.SpacingM, f.OrientationDeg, f.AntiHandling, f.KnownAtStartSides));
        Assert.NotEqual(7u, f.Seed); Assert.NotEqual(0u, f.Seed);
        Assert.True(MinefieldOps.SetKnownAtStart(w, e, 1, false)); Assert.Equal(0, w.GetComponentRO<Minefield>(e).KnownAtStartSides);
        Assert.False(MinefieldOps.SetType(w, Entity.Null, MineType.Mixed));
    }

    [Fact]
    public void Spacing_IsKeptIn2To100_Orientation_WrapsInto0To360()
    {
        var (w, e, _) = World();
        MinefieldOps.SetSpacing(w, e, 0.5f); Assert.Equal(2f, w.GetComponentRO<Minefield>(e).SpacingM);
        MinefieldOps.SetSpacing(w, e, 500f); Assert.Equal(100f, w.GetComponentRO<Minefield>(e).SpacingM);
        Assert.False(MinefieldOps.SetSpacing(w, e, float.NaN));
        foreach (var (input, expected) in new[] { (-10f, 350f), (360f, 0f), (725f, 5f), (-360f, 0f), (359.5f, 359.5f) })
        {
            MinefieldOps.SetOrientation(w, e, input + 1f);                    // a different value first, so every case is a real change
            MinefieldOps.SetOrientation(w, e, input);
            Assert.Equal(expected, w.GetComponentRO<Minefield>(e).OrientationDeg);
        }
    }

    [Fact]
    public void Lanes_AreAdded_RefusedAtEight_AndDeletedWithTheRestCompacted()
    {
        var (w, e, rec) = World();
        for (int i = 0; i < Minefield.MaxLanes; i++) Assert.True(MinefieldOps.AddLane(w, e, new Vector2(1000 + i, 2000), new Vector2(1000 + i, 2100)));
        Assert.Equal("begin Add lane", rec.Log[0]);
        rec.Log.Clear();
        Assert.False(MinefieldOps.AddLane(w, e, Vector2.Zero, Vector2.One * 50f));          // a ninth: refused, no step
        Assert.Empty(rec.Log);
        Assert.False(MinefieldOps.AddLane(w, e, Vector2.One, Vector2.One));                 // a point is no lane
        Assert.Equal(Minefield.MaxLanes, w.GetComponentRO<Minefield>(e).LaneCount);
        Assert.Equal(4.5f, w.GetComponentRO<Minefield>(e).Lane(0).Width);
        Assert.True(MinefieldOps.DeleteLane(w, e, 2));
        Assert.Equal(new[] { "begin Delete lane", "end" }, rec.Log);
        var f = w.GetComponentRO<Minefield>(e);
        Assert.Equal(7, f.LaneCount);
        for (int i = 0; i < 7; i++) Assert.Equal(1000f + (i < 2 ? i : i + 1), f.Lane(i).A.X);   // 0 1 3 4 5 6 7: the later lanes moved up
        Assert.Equal(default, f.Lane(7));                                                    // the freed slot is clean
        Assert.False(MinefieldOps.DeleteLane(w, e, 7));
        Assert.False(MinefieldOps.DeleteLane(w, e, -1));
    }

    [Fact]
    public void TheShapeLock_ComesFromAStruckMineOfThisField_WithItsReason_AClearedOneOrAnotherFieldsDoesNotLock()
    {
        var (w, e, _) = World();
        Assert.False(MinefieldOps.ShapeLock(w, e, out var none)); Assert.Equal("", none);
        Strike(w, FieldGuid, MineGone.Cleared);
        Strike(w, Guid.NewGuid(), MineGone.Struck);                                          // another field's
        Assert.False(MinefieldOps.ShapeLock(w, e, out _));
        Strike(w, FieldGuid, MineGone.Struck, 5, 6);
        Assert.True(MinefieldOps.ShapeLock(w, e, out var reason));
        Assert.Equal("Shape locked: 1 mine(s) have gone off in this field - delete and re-lay it to change the outline", reason);
        Assert.Equal(reason, OutlineEditPolicy.ReasonFor(w, e));
        // type, spacing and lanes still edit
        Assert.True(MinefieldOps.SetType(w, e, MineType.AntiPersonnel)); Assert.True(MinefieldOps.AddLane(w, e, Vector2.Zero, new Vector2(50, 0)));
    }

    [Fact]
    public void TheCounts_AreDerivedFromThePattern_TheStrikes_AndTheLanes_AndASteadyFrameAllocatesNothing()
    {
        var (w, e, _) = World();
        var c = MinefieldOps.Counts(w, e);
        var anchor = new Vector2(1000f, 2000f); var pts = Outline.Select(p => anchor + p).ToList(); var mines = new List<Mine>();
        MinePattern.MinesIn(w.GetComponentRO<Minefield>(e), anchor, pts, mines);
        Assert.Equal(mines.Count, c.Total);
        Assert.Equal(mines.Count(m => m.Type == MineType.AntiPersonnel), c.AntiPersonnel);
        Assert.Equal(mines.Count(m => m.Type == MineType.AntiTank), c.AntiTank);
        Assert.True(c.Total > 50 && c.AntiPersonnel > 0 && c.AntiTank > 0 && c.Struck == 0 && c.Cleared == 0, $"{c}");
        // a strike is counted; a lane clears the mines it covers; a spacing change re-derives
        Strike(w, FieldGuid, MineGone.Struck, mines[0].Row, mines[0].Col);
        Assert.Equal(1, MinefieldOps.Counts(w, e).Struck);
        MinefieldOps.AddLane(w, e, anchor + new Vector2(20, 60), anchor + new Vector2(240, 40));
        var withLane = MinefieldOps.Counts(w, e);
        Assert.Equal(c.Total, withLane.Total);
        Assert.True(withLane.Cleared > 0, "the lane clears the mines under it");
        MinefieldOps.SetSpacing(w, e, 20f);
        Assert.True(MinefieldOps.Counts(w, e).Total < c.Total);
        // steady: 0 B
        MinefieldOps.Counts(w, e);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) MinefieldOps.Counts(w, e);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(default, MinefieldOps.Counts(w, Entity.Null));
    }

    [Fact]
    public void TheAreaTool_BirthsAMinefield_WithItsDefaults_AndNeverTheGenericOverlayStyle_AndThePickerOffersIt()
    {
        var info = Assert.Single(PlacementToolRegistry.Graphics, g => (long)g.TkbType == Minefields.TkbType);
        Assert.Equal((PlacementToolKind.Area, "Minefield"), (info.Tool, info.Name));
        var manager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer());
        var arm = new Hrot.ScenarioEditor.Tools.AreaAuthoringArm(manager);
        var made = new List<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>();
        arm.Arm(new Hrot.ScenarioEditor.Tools.AreaAuthoringRequest((TkbTypeId)Minefields.TkbType, "{}", made.Add));
        var gizmo = arm.ActiveGizmo!;
        foreach (var p in new[] { new Vector2(100f, 100f), new Vector2(200f, 100f), new Vector2(150f, 180f) })
        {
            gizmo.OnDragUpdate(new Vector3(p.X, p.Y, 0f));
            gizmo.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, new Vector3(p.X, p.Y, 0f));
        }
        gizmo.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Enter, true);
        var cmd = Assert.Single(made);
        Assert.DoesNotContain(cmd.InitialComponents!, c => c is MapOverlayStyle);               // MR-8: MinefieldGizmo draws it, never the generic overlay
        var f = Assert.Single(cmd.InitialComponents!.OfType<Minefield>());
        Assert.Equal((MineType.Mixed, 10f, 0f, 1u), (f.Type, f.SpacingM, f.OrientationDeg, f.Seed));
        Assert.Equal(3, Assert.Single(cmd.InitialComponents!.OfType<EditablePolyline>()).Points.Count);
        // and a tactical area is unchanged: it keeps its style and gets no Minefield
        var plain = Hrot.ScenarioEditor.Tools.AreaAuthoringArm.BornWith(TkbEntityTypes.TacGraphic_Area, new EditablePolyline { Points = new List<Vector2>() }, MapOverlayStyle.Default());
        Assert.Contains(plain, c => c is MapOverlayStyle); Assert.DoesNotContain(plain, c => c is Minefield);
    }

    private static GizmoPickToken Token(int net, uint sub) => new() { AnchorId = net, SubElementId = sub };

    [Fact]
    public void TheVertexEditor_RefusesToMoveInsertOrDelete_APointOfALockedField_ButMovesAnUnlockedOne()
    {
        var (w, e, rec) = World();
        const int net = 77;
        List<Vector2> Points() => ((Fdp.ModuleHost.Abstractions.ISimulationView)w).GetManagedComponentRO<EditablePolyline>(e).Points!.ToList();
        void Drag(VertexEditGizmo g, uint handle, Vector2 to)
        {
            var at = new Vector2(1000f, 2000f) + to;
            g.OnInteractionStarted(Token(net, handle), new Vector3(at.X, at.Y, 0f));
            g.OnDragUpdate(new Vector3(at.X, at.Y, 0f));
            g.OnCommit(new Vector3(at.X, at.Y, 0f));
        }
        // unlocked: the drag moves point 1 (handle 2) in ONE step — the control that proves the lock is what refuses
        using (var g = new VertexEditGizmo(w, e, net, () => { })) { Drag(g, 2, new Vector2(300f, -40f)); }
        Assert.Equal(new Vector2(300f, -40f), Points()[1]);
        Assert.Equal(new[] { "begin Move point", "end" }, rec.Log);

        Strike(w, FieldGuid, MineGone.Struck);
        rec.Log.Clear();
        var before = Points();
        using (var g = new VertexEditGizmo(w, e, net, () => { }))
        {
            Drag(g, 2, new Vector2(400f, 0f));                                   // move: refused
            Assert.Equal(before, Points());
            g.OnMenuAction(1);                                                   // insert after the selected point: refused
            g.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Delete, true);   // delete it: refused
            Assert.Equal(before, Points());
        }
        Assert.Empty(rec.Log);                                                   // no edit step at all
    }
}
