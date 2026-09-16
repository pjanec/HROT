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
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = new Vector3(10f, 0f, 10f), TargetSpeed = 5f });
            wps.Add(new RouteWaypoint { Position = new Vector3(20f, 0f, 20f), TargetSpeed = 5f });
            wps.Add(new RouteWaypoint { Position = new Vector3(30f, 0f, 10f), TargetSpeed = 5f });
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
        gizmo.OnDragUpdate(new Vector3(50f, 60f, 0f));      // worldX=50, worldY=60 -> Z=60
        gizmo.OnCommit(Vector3.Zero);

        var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(_entity);
        var wp0  = plan.Waypoints![0];

        Assert.Equal(50f, wp0.Position.X, precision: 3);
        Assert.Equal(60f, wp0.Position.Z, precision: 3);
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
        Assert.Equal(20f, plan.Waypoints![1].Position.Z, precision: 3);
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
        public void DrawEntityLocal(long a, Vector3 ls, Vector3 le,
            Rgba32 c, float t = 1f, byte l = 0) { }
        public void DrawEntityLocalInteractive(long a, Vector3 ls, Vector3 le,
            Rgba32 c, ushort sid, float t = 1f, byte l = 0) { }
    }
}
