using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Hrot.Map.Common.Components;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="VertexEditGizmo"/> interaction state machine (GIZMOS1-T010).
/// </summary>
public class VertexEditGizmoTests : IDisposable
{
    // -- No-op IDebugDrawBuilder stub. ⭐ Also COUNTS, for the §4.7i focus rails below: EmitRaw and
    //    DrawContextMenuBinding are DEFAULT interface methods, which is why the original stub omitted
    //    them and why adding them here changes nothing for the existing tests. --
    private sealed class NullDraw : Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder
    {
        public int Lines;          // the WORK IN PROGRESS — the edited shape's edges
        public int Handles;        // the AFFORDANCE — one raw Box2D per vertex
        public int MenuBindings;   // the handle-anchored context menu

        public void EmitRaw(in DebugPrimitive prim) => Handles++;
        public void DrawContextMenuBinding(int networkId, string menuJson) => MenuBindings++;

        public void DrawLine(Vector3 s, Vector3 e, Rgba32 c, float t = 1f,
            SizeMode m = SizeMode.ScreenPixels,
            PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid)
            => Lines++;
        public void DrawLineGradient(Vector3 s, Vector3 e, Rgba32 sc, Rgba32 ec, float t = 1f,
            SizeMode m = SizeMode.ScreenPixels,
            PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid) { }
        public void DrawSphere(Vector3 c, float r, Rgba32 col,
            float thickness = 0f, SizeMode sm2 = SizeMode.WorldMeters,
            PipelineTarget tg = PipelineTarget.All, byte l = 0,
            Rgba32 fillColor = default, LineStyle style = LineStyle.Solid) { }
        public void DrawArrow(Vector3 f, Vector3 t, Rgba32 c, float h = 1f, byte l = 0) { }
        public void DrawText(float x, float y, Fdp.Core.FixedString32 t, Rgba32 c,
            CoordinateSpace sp = CoordinateSpace.World, byte l = 0, float fontSizePx = 0f, float lineOffsetPx = 0f) { }
        public void DrawTextLong(float x, float y, string t, Rgba32 c,
            CoordinateSpace sp = CoordinateSpace.World, byte l = 0, float fontSizePx = 0f, float lineOffsetPx = 0f) { }
        public void DrawEntityBadge(Entity e, Fdp.Core.FixedString32 rt,
            PipelineTarget tg = PipelineTarget.All) { }
        public void DrawEntityLocal(int a, Vector3 ls, Vector3 le,
            Rgba32 c, float t = 1f, byte l = 0) { }
        public void DrawEntityLocalInteractive(int a, Vector3 ls, Vector3 le,
            Rgba32 c, ushort sid, float t = 1f, byte l = 0) { }
    }

    private readonly EntityRepository _repo;
    private readonly Entity           _entity;
    private const int NetworkId = 42;

    public VertexEditGizmoTests()
    {
        _repo = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_repo);
        _repo.RegisterManagedComponent<EditablePolyline>();

        _entity = _repo.CreateEntity();
        _repo.AddComponent(_entity, default(SimTransform));
        _repo.AddComponent(_entity, new NetworkIdentity { Value = NetworkId });

        var poly = new EditablePolyline
        {
            Points = new List<Vector2>
            {
                new Vector2(10f, 10f),
                new Vector2(20f, 20f),
                new Vector2(30f, 10f),
            }
        };
        _repo.SetManagedComponent(_entity, poly);
    }

    public void Dispose() { }

    private VertexEditGizmo CreateGizmo()
        => new VertexEditGizmo(_repo, _entity, NetworkId, onRemove: () => { });

    private static GizmoPickToken Token(uint subElementId)
        => new GizmoPickToken { AnchorId = NetworkId, SubElementId = subElementId };

    // -- VEG-001 --

    /// <summary>
    /// OnInteractionStarted with SubElementId=2 selects vertex at index 1;
    /// a subsequent OnDragUpdate moves that vertex.
    /// </summary>
    [Fact]
    public void OnInteractionStarted_SetsActiveVertex()
    {
        using var gizmo = CreateGizmo();

        // SubElementId=2 -> vertex index 1.
        gizmo.OnInteractionStarted(Token(2), Vector3.Zero);
        gizmo.OnDragUpdate(new Vector3(99f, 77f, 0f));
        gizmo.OnCommit(Vector3.Zero);

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);

        // Index 1 was moved; indices 0 and 2 are unchanged.
        Assert.Equal(new Vector2(10f, 10f), poly.Points[0]);
        Assert.Equal(new Vector2(99f, 77f), poly.Points[1]);
        Assert.Equal(new Vector2(30f, 10f), poly.Points[2]);
    }

    // -- VEG-002 --

    /// <summary>
    /// After drag + OnCommit, the EditablePolyline in the ECS repo reflects the moved vertex.
    /// </summary>
    [Fact]
    public void OnCommit_WritesBackToEcs()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // vertex 0
        gizmo.OnDragUpdate(new Vector3(55f, 55f, 0f));
        gizmo.OnCommit(Vector3.Zero);

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        Assert.Equal(new Vector2(55f, 55f), poly.Points[0]);
        Assert.Equal(3, poly.Points.Count);
    }

    // -- VEG-003 --

    /// <summary>
    /// After drag + OnCancel, the EditablePolyline Points are unchanged from initial.
    /// </summary>
    [Fact]
    public void OnCancel_RevertsVertex()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(2), Vector3.Zero); // vertex 1
        gizmo.OnDragUpdate(new Vector3(999f, 999f, 0f));
        gizmo.OnCancel();

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        // Points[1] must still be (20, 20).
        Assert.Equal(new Vector2(20f, 20f), poly.Points[1]);
    }

    // -- VEG-004 --

    /// <summary>
    /// OnMenuAction(1) after selecting vertex 0 inserts a midpoint; Points.Count increases by 1.
    /// </summary>
    [Fact]
    public void OnMenuAction_InsertAfter_AddsVertex()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // vertex 0
        gizmo.OnMenuAction(1); // insert after

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        Assert.Equal(4, poly.Points.Count);
    }

    // -- VEG-005 --

    /// <summary>
    /// OnMenuAction(2) after selecting vertex 0 removes that vertex; Points.Count decreases by 1.
    /// ⭐ B-17 P6: an area keeps its 3 points — so the area is first given a 4th (the same hold the Delete key has: <c>P6_DeleteAtTheMinimum_Refuses_…</c>).
    /// </summary>
    [Fact]
    public void OnMenuAction_Delete_RemovesVertex()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // vertex 0
        gizmo.OnMenuAction(1); // insert after → 4 points
        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // vertex 0 again
        gizmo.OnMenuAction(2); // delete

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        Assert.Equal(3, poly.Points.Count);
        gizmo.OnMenuAction(2); // delete at the minimum (nothing selected now / 3 points) — refused or no-op
        Assert.Equal(3, ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity).Points.Count);
    }

    // -- §4.7i — A SUSPENDED TOOL DRAWS ITS WORK, NOT ITS HANDLES (user ruling, 2026-09-10) --

    /// <summary>
    /// 🔒 The baseline half of the ruling: while this gizmo HOLDS focus it draws both the edited shape
    /// and a handle per vertex, plus the handle-anchored context menu.
    /// <para>📐 IsFocused is granted at ARM time (DataDrivenGizmoSystem.ActivateGizmo:94 → TryGrant →
    /// SetFocus(true):63), so this is the state a freshly armed tool is in — which is why hiding handles
    /// on !IsFocused cannot hide them from an operator who just armed the tool.</para>
    /// </summary>
    [Fact]
    public void AFocusedGizmo_DrawsItsShapeAndItsHandles()
    {
        using var gizmo = CreateGizmo();
        gizmo.OnInteractionStarted(Token(1), Vector3.Zero);   // activates
        gizmo.SetFocus(true);

        var draw = new NullDraw();
        gizmo.UpdateAndDraw(_repo, 0.016f, draw);

        Assert.Equal(3 + 1, draw.Handles);    // one per vertex, and (B-17 P6) the centre handle
        Assert.Equal(1, draw.MenuBindings);
        Assert.True(draw.Lines > 0, "the edited shape must be drawn");
    }

    /// <summary>
    /// 🔒🔒 THE RULING: <i>"handles gone entirely while suspended... The partial route or shape edited
    /// should stay drawn."</i> ⛔ Not dimmed — ABSENT.
    /// <para>⭐⭐ This rail is ALSO the safety proof for <c>CE-259r</c>. That change reorders the gizmo
    /// group so the entity emitters run first, which flips the layer-0 z-order tiebreak in favour of
    /// HANDLES (DebugGizmoLayer.cs:510). Correct for an ACTIVE tool — but a SUSPENDED tool's handles
    /// would then steal the entity picker's hover, and the picker hit-tests UNFILTERED by design so it
    /// has no capture filter to shield it. If this rail reddens, CE-259r becomes a mis-pick.</para>
    /// </summary>
    [Fact]
    public void ASuspendedGizmo_DrawsItsShape_ButNoHandlesAndNoMenu()
    {
        using var gizmo = CreateGizmo();
        gizmo.OnInteractionStarted(Token(1), Vector3.Zero);
        gizmo.SetFocus(true);
        gizmo.SetFocus(false);                                 // what GizmoFocusRegistry.Suspend does

        var draw = new NullDraw();
        gizmo.UpdateAndDraw(_repo, 0.016f, draw);

        Assert.Equal(0, draw.Handles);
        Assert.Equal(0, draw.MenuBindings);
        Assert.True(draw.Lines > 0, "the work in progress must STAY drawn while suspended");
    }

    /// <summary>
    /// ⭐ The other half of the ruling — <i>"and re-appear once focus returns."</i>
    /// </summary>
    [Fact]
    public void ResumingFocus_BringsTheHandlesBack()
    {
        using var gizmo = CreateGizmo();
        gizmo.OnInteractionStarted(Token(1), Vector3.Zero);
        gizmo.SetFocus(false);
        gizmo.SetFocus(true);                                  // Resume

        var draw = new NullDraw();
        gizmo.UpdateAndDraw(_repo, 0.016f, draw);

        Assert.Equal(3 + 1, draw.Handles);   // the three vertices and (B-17 P6) the centre handle
        Assert.Equal(1, draw.MenuBindings);
    }

    // ── ⭐ B-17 P2 — the drawing convention (D11) on the vertex editor: ONE session, three finish doors, right-click = the menu ────────────

    private VertexEditGizmo CreateGizmo(Action onRemove) => new VertexEditGizmo(_repo, _entity, NetworkId, onRemove);

    private List<Vector2> Points() => ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity).Points.ToList();

    /// <summary>Enter, the on-map ✓ and the menu's Finish all write back what is on the screen (a drag still in progress included) and end the tool; one
    /// removal each. Red-proof (compiles): <c>Resolve</c> not calling <c>_onRemove</c> on <c>Finished</c> ⇒ the removal counts read 0.</summary>
    [Fact]
    public void P2_Enter_the_tick_and_the_menu_Finish_all_write_back_and_end_the_tool()
    {
        foreach (var door in new[] { "enter", "tick", "menu" })
        {
            var poly0 = Points();
            int removed = 0;
            using var gizmo = CreateGizmo(() => removed++);
            gizmo.OnInteractionStarted(Token(1), Vector3.Zero);
            gizmo.OnDragUpdate(new Vector3(55f, 55f, 0f));          // a drag not yet committed
            switch (door)
            {
                case "enter": gizmo.OnKeyEvent(MapKeyboardKey.Enter, true); break;
                case "tick": var d = gizmo.Session.DonePosition; gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(d.X, d.Y, 0f)); break;
                default: gizmo.OnMenuAction(Hrot.Common.Constants.GlobalActionIds.FinishDrawing); break;
            }
            Assert.True(removed == 1, $"{door}: the tool ended");
            Assert.True(Points()[0] == new Vector2(55f, 55f), $"{door}: written back");
            Assert.Equal(3, Points().Count);
            // restore for the next door
            _repo.SetManagedComponent(_entity, new EditablePolyline { Points = poly0 });
        }
    }

    /// <summary>Esc and the menu's Cancel undo the drag in progress and end the tool; the polyline is as it was.</summary>
    [Fact]
    public void P2_Esc_and_the_menu_Cancel_undo_the_drag_and_end_the_tool()
    {
        foreach (var door in new[] { "esc", "menu" })
        {
            int removed = 0;
            using var gizmo = CreateGizmo(() => removed++);
            gizmo.OnInteractionStarted(Token(2), Vector3.Zero);
            gizmo.OnDragUpdate(new Vector3(999f, 999f, 0f));
            if (door == "esc") gizmo.OnKeyEvent(MapKeyboardKey.Escape, true); else gizmo.OnMenuAction(Hrot.Common.Constants.GlobalActionIds.CancelDrawing);
            Assert.True(removed == 1, door);
            Assert.Equal(new Vector2(20f, 20f), Points()[1]);
        }
    }

    /// <summary>
    /// ⭐⭐ Right-click is the MENU: press and release end NOTHING and write nothing (a right-RELEASE used to write back and exit); Backspace is not an editor key.
    /// The menu carries the editor's own two rows then the catalogue's Finish · Cancel. Red-proof (compiles): <c>OnMouseEvent</c> exiting on a right-release again ⇒
    /// <c>removed</c> reads 1.
    /// </summary>
    [Fact]
    public void P2_RightClick_ends_nothing_and_the_menu_is_the_editors_rows_then_Finish_and_Cancel()
    {
        int removed = 0;
        using var gizmo = CreateGizmo(() => removed++);
        gizmo.OnInteractionStarted(Token(1), Vector3.Zero);
        gizmo.OnDragUpdate(new Vector3(77f, 77f, 0f));
        gizmo.OnMouseEvent(MapMouseButton.Right, true, Vector3.Zero);
        gizmo.OnMouseEvent(MapMouseButton.Right, false, Vector3.Zero);
        gizmo.OnKeyEvent(MapKeyboardKey.Backspace, true);
        Assert.Equal(0, removed);
        Assert.Equal(new Vector2(10f, 10f), Points()[0]);           // nothing was written
        Assert.Equal(3, gizmo.Session.Points.Count);

        var rows = System.Text.Json.JsonSerializer.Deserialize<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto[]>(
            gizmo.Session.Menu!.Json, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
        Assert.Equal(new[] { "Insert point after", "Delete point", null, "Finish", "Cancel" }, rows.Select(r => r.Label).ToArray());
    }
    // ── ⭐ B-17 P6 — the centre handle, click a segment = insert, Delete = remove the selected point (each one edit, one undo step) ────────────

    private sealed class UndoRig
    {
        public readonly Hrot.Presentation.Edits.EditHistory History = new();
        public double Now;
        public UndoRig(EntityRepository repo)
        {
            repo.Tick();
            EditSteps.Install(repo, new Hrot.Presentation.Edits.WorldUndoRecorder(History, new Fdp.Toolkit.Replay.WorldSnapshotRing(), () => true, () => Now));
        }
    }

    private static readonly Vector2[] Original = { new(10f, 10f), new(20f, 20f), new(30f, 10f) };

    /// <summary>The centre handle moves EVERY vertex by the cursor's travel and the finished drag is ONE edit and ONE undo step — however many updates it took.
    /// 🔴 Red-proof (compiles): <c>OnDragUpdate</c> also calling <c>WriteBackAndPublish("Move shape")</c> ⇒ N steps, one Undo restores only the last ⇒ red.</summary>
    [Fact]
    public void P6_TheCentreHandle_MovesEveryVertex_AsOneEditAndOneUndoStep()
    {
        var rig = new UndoRig(_repo);
        using var gizmo = CreateGizmo();
        var centre = gizmo.Session.Centre;
        gizmo.OnInteractionStarted(Token(Fdp.Toolkit.Vis2D.Gizmos.ShapeEditSession.CentreHandleId), new Vector3(centre.X, centre.Y, 0f));
        foreach (var step in new[] { 10f, 20f, 30f })
        {
            rig.Now += 5.0;                                          // beyond the coalescing second: N edits would be N steps
            gizmo.OnDragUpdate(new Vector3(centre.X + step, centre.Y + step * 2f, 0f));
        }
        gizmo.OnCommit(Vector3.Zero);

        Assert.Equal(Original.Select(p => p + new Vector2(30f, 60f)).ToList(), Points());
        Assert.Equal(1, rig.History.Count);
        Assert.True(rig.History.Undo());
        Assert.Equal(Original.ToList(), Points());
        Assert.True(rig.History.Redo());
        Assert.Equal(Original.Select(p => p + new Vector2(30f, 60f)).ToList(), Points());
    }

    /// <summary>Esc during a centre drag puts every vertex back and writes nothing; a click on the handle that moved nothing is no edit.</summary>
    [Fact]
    public void P6_TheCentreDrag_CancelsClean_AndAClickOnTheHandleIsNoEdit()
    {
        var rig = new UndoRig(_repo);
        int removed = 0;
        using var gizmo = CreateGizmo(() => removed++);
        var c = gizmo.Session.Centre;
        gizmo.OnInteractionStarted(Token(Fdp.Toolkit.Vis2D.Gizmos.ShapeEditSession.CentreHandleId), new Vector3(c.X, c.Y, 0f));
        gizmo.OnDragUpdate(new Vector3(c.X + 99f, c.Y + 99f, 0f));
        gizmo.OnKeyEvent(MapKeyboardKey.Escape, true);
        Assert.Equal(1, removed);
        Assert.Equal(Original.ToList(), gizmo.Session.Points.ToList());
        Assert.Equal(Original.ToList(), Points());

        using var g2 = CreateGizmo();
        g2.OnInteractionStarted(Token(Fdp.Toolkit.Vis2D.Gizmos.ShapeEditSession.CentreHandleId), new Vector3(c.X, c.Y, 0f));
        g2.OnCommit(Vector3.Zero);
        Assert.Equal(0, rig.History.Count);
    }

    /// <summary>The centre handle is DRAWN, as one more Box2D handle at the vertices' mean carrying its own sub-element id.</summary>
    [Fact]
    public void P6_TheCentreHandleIsDrawn_AtTheMean_WithItsOwnId()
    {
        using var gizmo = CreateGizmo();
        gizmo.SetFocus(true);
        var draw = new IdDraw();
        gizmo.UpdateAndDraw(_repo, 0.016f, draw);
        var centre = draw.Handles.Single(h => h.Id == Fdp.Toolkit.Vis2D.Gizmos.ShapeEditSession.CentreHandleId);
        Assert.Equal(20f, centre.X, precision: 3);
        Assert.Equal(40f / 3f, centre.Y, precision: 3);
        Assert.Equal(3, draw.Handles.Count(h => h.Id != Fdp.Toolkit.Vis2D.Gizmos.ShapeEditSession.CentreHandleId));
    }

    private sealed class IdDraw : Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder
    {
        public readonly List<(uint Id, float X, float Y)> Handles = new();
        public void EmitRaw(in DebugPrimitive prim) => Handles.Add((prim.SubElementId, prim.BoxCenterX, prim.BoxCenterY));
        public void DrawContextMenuBinding(int networkId, string menuJson) { }
        public void DrawLine(Vector3 s, Vector3 e, Rgba32 c, float t = 1f, SizeMode m = SizeMode.ScreenPixels, PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid) { }
        public void DrawLineGradient(Vector3 s, Vector3 e, Rgba32 sc, Rgba32 ec, float t = 1f, SizeMode m = SizeMode.ScreenPixels, PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid) { }
        public void DrawSphere(Vector3 c, float r, Rgba32 col, float thickness = 0f, SizeMode sm2 = SizeMode.WorldMeters, PipelineTarget tg = PipelineTarget.All, byte l = 0, Rgba32 fillColor = default, LineStyle style = LineStyle.Solid) { }
        public void DrawArrow(Vector3 f, Vector3 t, Rgba32 c, float h = 1f, byte l = 0) { }
        public void DrawText(float x, float y, Fdp.Core.FixedString32 t, Rgba32 c, CoordinateSpace sp = CoordinateSpace.World, byte l = 0, float fontSizePx = 0f, float lineOffsetPx = 0f) { }
        public void DrawTextLong(float x, float y, string t, Rgba32 c, CoordinateSpace sp = CoordinateSpace.World, byte l = 0, float fontSizePx = 0f, float lineOffsetPx = 0f) { }
        public void DrawEntityBadge(Entity e, Fdp.Core.FixedString32 rt, PipelineTarget tg = PipelineTarget.All) { }
        public void DrawEntityLocal(int a, Vector3 ls, Vector3 le, Rgba32 c, float t = 1f, byte l = 0) { }
        public void DrawEntityLocalInteractive(int a, Vector3 ls, Vector3 le, Rgba32 c, ushort sid, float t = 1f, byte l = 0) { }
    }

    /// <summary>A click on a SEGMENT inserts a point exactly on it, between its two vertices (the closing segment appends), and selects it: one edit, one undo step. A click on a vertex, the
    /// centre or open ground inserts nothing.</summary>
    [Fact]
    public void P6_AClickOnASegment_InsertsBetweenTheRightTwoPoints_OneUndoStep_AndNothingElseDoes()
    {
        var rig = new UndoRig(_repo);
        using var gizmo = CreateGizmo();
        void Click(float x, float y) => gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(x, y, 0f));

        Click(10.5f, 10.4f);   // on vertex 0
        Click(20f, 13.3f);     // on the centre handle
        Click(15f, 40f);       // open ground
        Assert.Equal(0, rig.History.Count);
        Assert.Equal(3, Points().Count);

        Click(15.5f, 14.5f);   // 0.7 m off the first segment (10,10)-(20,20)
        var after = Points();
        Assert.Equal(4, after.Count);
        Assert.Equal(new Vector2(10f, 10f), after[0]);
        Assert.Equal(15f, after[1].X, precision: 3);               // projected ONTO the segment
        Assert.Equal(15f, after[1].Y, precision: 3);
        Assert.Equal(new Vector2(20f, 20f), after[2]);
        Assert.Equal(1, gizmo.Session.Selected);
        Assert.Equal(1, rig.History.Count);

        rig.Now += 5.0;
        Click(25f, 10.3f);     // the closing segment (30,10)-(10,10): appends
        Assert.Equal(5, Points().Count);
        Assert.Equal(25f, Points()[4].X, precision: 3);
        Assert.Equal(2, rig.History.Count);

        Assert.True(rig.History.Undo());
        Assert.Equal(4, Points().Count);
        Assert.True(rig.History.Undo());
        Assert.Equal(Original.ToList(), Points());
    }

    /// <summary>Delete (a row of the keymap) removes the selected vertex — one edit, one undo step; nothing selected ⇒ nothing happens.
    /// 🔴 Red-proof (compiles): <c>DeleteSelectedPoint</c> not calling <c>WriteBackAndPublish</c> ⇒ the stored polyline keeps 4 points ⇒ red.</summary>
    [Fact]
    public void P6_Delete_RemovesTheSelectedVertex_OneUndoStep()
    {
        var rig = new UndoRig(_repo);
        using var gizmo = CreateGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(15.5f, 14.5f, 0f));      // insert → 4 points, the new one selected
        rig.Now += 5.0;
        gizmo.OnKeyEvent(MapKeyboardKey.Delete, true);                                       // a press is not the row's event
        Assert.Equal(4, Points().Count);
        gizmo.OnKeyEvent(MapKeyboardKey.Delete, false);                                      // the release
        Assert.Equal(Original.ToList(), Points());
        Assert.Equal(-1, gizmo.Session.Selected);
        Assert.Equal(2, rig.History.Count);
        Assert.True(rig.History.Undo());
        Assert.Equal(4, Points().Count);

        gizmo.OnKeyEvent(MapKeyboardKey.Delete, false);                                      // nothing is selected now ⇒ nothing happens
        Assert.Equal(4, Points().Count);
        Assert.Equal(2, rig.History.Count);
    }

    /// <summary>Delete at the MINIMUM (an area keeps 3 points) removes nothing, records nothing, and says why — on the tool's hint line. 🔴 Red-proof (compiles): the
    /// minimum check removed from <c>DeleteSelectedPoint</c> ⇒ the area drops to 2 points ⇒ red.</summary>
    [Fact]
    public void P6_DeleteAtTheMinimum_Refuses_WithTheReasonInTheHint()
    {
        var rig = new UndoRig(_repo);
        var global = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer());
        var tools = new Hrot.ScenarioEditor.Tools.ToolController(() => global, () => null);
        tools.Register(new Hrot.ScenarioEditor.Tools.ToolDescriptor("p6-edit", "Edit", Hrot.ScenarioEditor.Tools.ToolModality.Modal, Hrot.ScenarioEditor.Tools.ToolArbiter.None, Hint: "the tool's hint"),
                       _ => Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed);
        Assert.True(tools.Activate("p6-edit"));
        Hrot.ScenarioEditor.Map.MapUiStates.Set(_repo, tools);

        using var gizmo = CreateGizmo();
        gizmo.OnInteractionStarted(Token(2), Vector3.Zero);
        gizmo.OnCommit(Vector3.Zero);                                                        // a click: vertex 1 is selected
        gizmo.OnKeyEvent(MapKeyboardKey.Delete, false);

        Assert.Equal(3, Points().Count);
        Assert.Equal(3, gizmo.Session.Points.Count);
        Assert.Equal(1, gizmo.Session.Selected);
        Assert.Equal(0, rig.History.Count);
        Assert.Contains("needs 3 points", gizmo.Session.Reason);
        Assert.Equal(gizmo.Session.Reason, tools.ActiveHint);
        gizmo.OnInteractionStarted(Token(1), Vector3.Zero);                                  // touching a handle again clears it
        Assert.Equal("the tool's hint", tools.ActiveHint);
    }
}
