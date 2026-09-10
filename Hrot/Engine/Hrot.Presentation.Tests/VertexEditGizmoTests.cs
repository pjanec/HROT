using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
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
        public void DrawContextMenuBinding(long networkId, string menuJson) => MenuBindings++;

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
        public void DrawEntityLocal(Entity a, Vector3 ls, Vector3 le,
            Rgba32 c, float t = 1f, byte l = 0) { }
        public void DrawEntityLocalInteractive(Entity a, Vector3 ls, Vector3 le,
            Rgba32 c, ushort sid, float t = 1f, byte l = 0) { }
    }

    private readonly EntityRepository _repo;
    private readonly Entity           _entity;
    private const long NetworkId = 42L;

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
    /// </summary>
    [Fact]
    public void OnMenuAction_Delete_RemovesVertex()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // vertex 0
        gizmo.OnMenuAction(2); // delete

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        Assert.Equal(2, poly.Points.Count);
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

        Assert.Equal(3, draw.Handles);        // one per vertex
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

        Assert.Equal(3, draw.Handles);
        Assert.Equal(1, draw.MenuBindings);
    }
}
