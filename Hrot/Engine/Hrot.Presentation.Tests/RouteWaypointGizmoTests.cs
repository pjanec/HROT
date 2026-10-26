using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="RouteWaypointGizmo"/> interaction state machine (GIZMOS1-T011).
/// </summary>
public class RouteWaypointGizmoTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly Entity           _entity;
    private const long NetworkId = 77L;

    public RouteWaypointGizmoTests()
    {
        _repo = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_repo);
        _repo.RegisterManagedComponent<RoutePlan>();

        _entity = _repo.CreateEntity();
        _repo.AddComponent(_entity, default(SimTransform));
        _repo.AddComponent(_entity, new NetworkIdentity { Value = NetworkId });

        var plan = new RoutePlan();
        plan = plan.WithWaypoints(wps =>
        {
            // ⭐ CE-2172 — Z-up (X east, Y north), as the draw tool writes them; the third is a flyer's, 120 m up
            wps.Add(new RouteWaypoint { Position = new Vector3(10f, 10f, 0f), TargetSpeed = 5f });
            wps.Add(new RouteWaypoint { Position = new Vector3(20f, 20f, 0f), TargetSpeed = 5f });
            wps.Add(new RouteWaypoint { Position = new Vector3(30f, 10f, 120f), TargetSpeed = 5f });
        });
        _repo.SetManagedComponent(_entity, plan);
    }

    public void Dispose()
    {
        // Ensure Current is cleared between test runs.
        RouteWaypointGizmo.Current?.Dispose();
    }

    private RouteWaypointGizmo CreateGizmo()
        => new RouteWaypointGizmo(_repo, _entity, NetworkId, onRemove: () => { });

    private static GizmoPickToken Token(uint subElementId)
        => new GizmoPickToken { AnchorId = NetworkId, SubElementId = subElementId };

    // -- RWG-001 --

    /// <summary>
    /// OnInteractionStarted with SubElementId=1 sets SelectedVertexIndex to 0.
    /// </summary>
    [Fact]
    public void OnInteractionStarted_SetsActiveVertex()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero);

        Assert.Equal(0, gizmo.SelectedVertexIndex);
    }

    // -- RWG-002 --

    /// <summary>
    /// After drag + OnCommit, the RoutePlan waypoints in the ECS repo reflect the moved waypoint.
    /// </summary>
    [Fact]
    public void OnCommit_WritesBackToEcs()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // waypoint 0
        gizmo.OnDragUpdate(new Vector3(50f, 60f, 0f));      // worldX=50, worldY=60 -> X 50, Y 60 (⭐ CE-2172 — Y is north)
        gizmo.OnCommit(Vector3.Zero);

        var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(_entity);
        var wp0  = plan.Waypoints![0];

        Assert.Equal(50f, wp0.Position.X, precision: 3);
        Assert.Equal(60f, wp0.Position.Y, precision: 3);
        Assert.Equal(0f, wp0.Position.Z, precision: 3);
        Assert.Equal(3, plan.Waypoints!.Count);
    }

    // -- RWG-003 --

    /// <summary>
    /// After drag + OnCancel, the waypoint position is unchanged from initial.
    /// </summary>
    [Fact]
    public void OnCancel_RevertsWaypoint()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(2), Vector3.Zero); // waypoint 1
        gizmo.OnDragUpdate(new Vector3(999f, 999f, 0f));
        gizmo.OnCancel();

        // After cancel the active index is reset to -1.
        Assert.Equal(-1, gizmo.SelectedVertexIndex);

        // ECS data was never written during drag (cancel reverts the in-memory copy
        // without publishing), so waypoint 1 remains at its original position.
        var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(_entity);
        Assert.Equal(20f, plan.Waypoints![1].Position.X, precision: 3);
        Assert.Equal(20f, plan.Waypoints![1].Position.Y, precision: 3);
    }

    // -- ⭐ CE-2172 (R-182; DESIGN_Air_Operations §5, air hand-off U1) --

    /// <summary>
    /// ⭐⭐ CE-2172 — the route gizmo is Z-UP: a waypoint is drawn where it was drawn (its X / Y on the map) and a drag moves X / Y and keeps
    /// its height. 📌 Red before: the gizmo read Position.Z as north, so a flyer's waypoint 120 m up was drawn 120 m north of where it is,
    /// and dragging it wrote the drag's north into its altitude.
    /// </summary>
    [Fact]
    public void CE2172_TheGizmoIsZUp_ADragMovesXY_AndKeepsTheHeight()
    {
        using var gizmo = CreateGizmo();
        gizmo.SetFocus(true);
        var draw = new CountingDraw();
        gizmo.UpdateAndDraw(_repo, 0.016f, draw);
        Assert.Contains(draw.Ends, p => p.X == 30f && p.Y == 10f && p.Z == 120f);   // the flyer's point, at its height
        Assert.Contains(draw.HandleCentres, c => c == new Vector2(30f, 10f));        // its handle on the map, where it was drawn

        gizmo.OnInteractionStarted(Token(3), Vector3.Zero);
        gizmo.OnDragUpdate(new Vector3(35f, 14f, 0f));
        gizmo.OnCommit(Vector3.Zero);
        var moved = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(_entity).Waypoints![2].Position;
        Assert.Equal(new Vector3(35f, 14f, 120f), moved);
    }

    // -- RWG-004 --

    /// <summary>
    /// RouteWaypointGizmo.Current is set on construction and cleared on Dispose.
    /// </summary>
    [Fact]
    public void Current_SetOnConstruction_ClearedOnDispose()
    {
        Assert.Null(RouteWaypointGizmo.Current);

        var gizmo = CreateGizmo();
        Assert.NotNull(RouteWaypointGizmo.Current);
        Assert.Same(gizmo, RouteWaypointGizmo.Current);

        gizmo.Dispose();
        Assert.Null(RouteWaypointGizmo.Current);
    }

    // -- ⭐ CE-1096 (ROUTES1 §11.4 — air hand-off U2) --

    /// <summary>
    /// ⭐⭐ CE-1096 — a waypoint STAYS selected after its click (press + release) and the waypoint panel shows it, air fields included; a
    /// field the panel changes is COMMITTED to the route at once — the route entity holds it and an update is published.
    /// 📌 Red before: OnCommit deselected (the panel went blank the moment the mouse came up) and the panel never wrote back.
    /// </summary>
    [Fact]
    public void CE1096_AClickedWaypointStaysSelected_AndThePanelsAirFieldsAreCommittedToTheRoute()
    {
        using var gizmo = CreateGizmo();
        gizmo.OnInteractionStarted(Token(2), Vector3.Zero);   // a click: press …
        gizmo.OnCommit(Vector3.Zero);                         // … and release
        Assert.Equal(1, gizmo.SelectedVertexIndex);

        var panel = new Hrot.UI.Common.Panels.WaypointEditorPanel(() => RouteWaypointGizmo.Current);
        var vm = panel.BuildViewModel("editor_waypoint_editor", "ig-waypoint-editor");
        Assert.True(vm.HasSelection);
        Assert.Equal(("Pass", 0f, "None"), (vm.Kind, vm.HeightAgl, vm.Hold));

        IRouteWaypointEditorState state = gizmo;
        ref var wp = ref state.GetSelectedWaypointRef();
        wp.Kind = Fdp.Toolkit.Navigation.RouteWaypointKind.Ground;
        wp.HeightAgl = 120f;
        wp.Hold = Fdp.Toolkit.Navigation.RouteHold.Pickup;
        wp.HoldUnit = 4242L;
        _repo.Bus.SwapBuffers();
        state.CommitEdits();

        var stored = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(_entity).Waypoints![1];
        Assert.Equal((Fdp.Toolkit.Navigation.RouteWaypointKind.Ground, 120f, Fdp.Toolkit.Navigation.RouteHold.Pickup, 4242L),
                     (stored.Kind, stored.HeightAgl, stored.Hold, stored.HoldUnit));
        _repo.Bus.SwapBuffers();
        Assert.NotEmpty(_repo.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.UpdateEntityCommand>());   // published
        vm = panel.BuildViewModel("editor_waypoint_editor", "ig-waypoint-editor");
        Assert.Equal(("Ground", 120f, "Pickup", 4242L), (vm.Kind, vm.HeightAgl, vm.Hold, vm.HoldUnit));
    }

    // -- §4.7i — A SUSPENDED TOOL DRAWS ITS WORK, NOT ITS HANDLES (user ruling, 2026-09-10) --

    /// <summary>
    /// 🔒 The twin of <c>VertexEditGizmoTests.ASuspendedGizmo_DrawsItsShape_ButNoHandlesAndNoMenu</c>.
    /// The route line is the work in progress and stays drawn; the waypoint handles and the
    /// handle-anchored context menu vanish entirely while focus is elsewhere.
    /// <para>⭐⭐ Also the safety proof for <c>CE-259r</c> on this gizmo — with the group reordered a
    /// layer-0 tie goes to HANDLES, so a suspended route editor's handles would steal the entity
    /// picker's hover. 📄 docs/UX/UX_Feature_Tool_Model.md §4.7i.</para>
    /// </summary>
    [Fact]
    public void FocusDecidesWhetherTheWaypointHandlesAreDrawnAtAll()
    {
        using var gizmo = CreateGizmo();
        gizmo.OnInteractionStarted(Token(1), Vector3.Zero);

        gizmo.SetFocus(true);
        var focused = new CountingDraw();
        gizmo.UpdateAndDraw(_repo, 0.016f, focused);

        gizmo.SetFocus(false);                                 // GizmoFocusRegistry.Suspend
        var suspended = new CountingDraw();
        gizmo.UpdateAndDraw(_repo, 0.016f, suspended);

        Assert.True(focused.Handles > 0, "a focused route editor draws its waypoint handles");
        Assert.Equal(1, focused.MenuBindings);

        Assert.Equal(0, suspended.Handles);
        Assert.Equal(0, suspended.MenuBindings);
        Assert.True(suspended.Lines > 0, "the route must STAY drawn while suspended");
    }

    /// <summary>⭐ Counting draw stub for the focus rails above.</summary>
    private sealed class CountingDraw : Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder
    {
        public int Lines;
        public int Handles;
        public int MenuBindings;
        public readonly List<Vector3> Ends = new();
        public readonly List<Vector2> HandleCentres = new();

        public void EmitRaw(in DebugPrimitive prim) { Handles++; HandleCentres.Add(new Vector2(prim.BoxCenterX, prim.BoxCenterY)); }
        public void DrawContextMenuBinding(long networkId, string menuJson) => MenuBindings++;

        public void DrawLine(Vector3 s, Vector3 e, Rgba32 c, float t = 1f,
            SizeMode m = SizeMode.ScreenPixels,
            PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid)
        { Lines++; Ends.Add(s); Ends.Add(e); }
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
        public void DrawEntityLocal(long a, Vector3 ls, Vector3 le,
            Rgba32 c, float t = 1f, byte l = 0) { }
        public void DrawEntityLocalInteractive(long a, Vector3 ls, Vector3 le,
            Rgba32 c, ushort sid, float t = 1f, byte l = 0) { }
    }
}
