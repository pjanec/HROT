using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Tools;
using Hrot.Map.Common.Components;
using Fdp.Kernel;
using Raylib_cs;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="RouteEditTool"/> — ROUTES1-T012.
/// Also covers the <c>GetSelectedWaypointRef</c> integration point used by
/// <c>WaypointEditorPanel</c> (ROUTES1-T013).
///
/// No DDS or Raylib window context required.
/// <see cref="RouteEditTool.Draw"/> is not exercised (requires Raylib window).
/// <see cref="RouteEditTool.OnEnter"/> is called with <c>null</c> canvas which is
/// null-safe for all input handler methods.
/// </summary>
public class RouteEditToolTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    // Three waypoints at well-known canvas positions (XZ plane → X, Z).
    private static readonly Vector3 Wp0Pos = new Vector3(100f, 0f, 100f);
    private static readonly Vector3 Wp1Pos = new Vector3(200f, 0f, 200f);
    private static readonly Vector3 Wp2Pos = new Vector3(300f, 0f, 150f);

    // Canvas 2D coords = (X, Z).
    private static Vector2 Canvas(Vector3 p) => new Vector2(p.X, p.Z);

    // Click very close to Wp1 (within pick radius).
    private static readonly Vector2 NearWp1 = new Vector2(
        Wp1Pos.X + RouteEditToolConstants.VertexPickRadius * 0.4f,
        Wp1Pos.Z);

    // Click far from all waypoints.
    private static readonly Vector2 FarClick = new Vector2(700f, 700f);

    // Mid-point of segment Wp0→Wp1 (for insertion test).
    private static Vector2 SegmentMidpoint => new Vector2(
        (Wp0Pos.X + Wp1Pos.X) / 2f + 1f, // nudge 1 px inside pick radius
        (Wp0Pos.Z + Wp1Pos.Z) / 2f);

    // ── Factory helpers ───────────────────────────────────────────────────────

    private static RoutePlan MakeThreeWaypointPlan()
    {
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = Wp0Pos, TargetSpeed = 10f });
            wps.Add(new RouteWaypoint { Position = Wp1Pos, TargetSpeed = 15f });
            wps.Add(new RouteWaypoint { Position = Wp2Pos, TargetSpeed = 20f });
        });
        return plan;
    }

    private static RouteEditTool CreateAndEnter(
        RoutePlan plan,
        Action<Entity, List<RouteWaypoint>>? onCommit = null)
    {
        var entity = new Entity(1, 0);
        onCommit ??= (_, _) => { };
        var tool = new RouteEditTool(entity, plan, onCommit);
        tool.OnEnter(null!); // canvas unused in headless tests
        return tool;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OnEnter — ghost initialisation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After <see cref="RouteEditTool.OnEnter"/> the ghost list must be a copy of
    /// the plan's waypoints with the same count and positions.
    /// </summary>
    [Fact]
    public void OnEnter_CopiesWaypointsIntoGhost()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());

        Assert.Equal(3, tool.GhostWaypoints.Count);
        Assert.Equal(Wp0Pos, tool.GhostWaypoints[0].Position);
        Assert.Equal(Wp1Pos, tool.GhostWaypoints[1].Position);
        Assert.Equal(Wp2Pos, tool.GhostWaypoints[2].Position);
    }

    /// <summary>
    /// <see cref="RouteEditTool.OnEnter"/> must reset <see cref="RouteEditTool.SelectedVertexIndex"/>
    /// to −1 (no selection).
    /// </summary>
    [Fact]
    public void OnEnter_ResetsSelectedVertexIndex()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());

        Assert.Equal(-1, tool.SelectedVertexIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HandleClick — vertex selection
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A left-click within <see cref="RouteEditToolConstants.VertexPickRadius"/> of
    /// Wp1 must select vertex 1.
    /// </summary>
    [Fact]
    public void HandleClick_Left_NearVertex1_SelectsVertex1()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());

        tool.HandleClick(NearWp1, MouseButton.Left);

        Assert.Equal(1, tool.SelectedVertexIndex);
    }

    /// <summary>
    /// A left-click on the midpoint of the segment Wp0→Wp1 (outside vertex pick
    /// radius) must insert a new waypoint at index 1, shifting Wp1 to index 2.
    /// The inserted waypoint inherits <c>TargetSpeed</c> from Wp0 (index 0).
    /// </summary>
    [Fact]
    public void HandleClick_Left_OnSegmentMidpoint_InsertsWaypointAtIndex1()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());

        // First ensure the midpoint is within segment pick range but outside vertex range.
        var mid = SegmentMidpoint;
        Assert.True(Vector2.Distance(mid, Canvas(Wp0Pos)) > RouteEditToolConstants.VertexPickRadius);
        Assert.True(Vector2.Distance(mid, Canvas(Wp1Pos)) > RouteEditToolConstants.VertexPickRadius);

        tool.HandleClick(mid, MouseButton.Left);

        Assert.Equal(4, tool.GhostWaypoints.Count); // was 3, now 4
        Assert.Equal(1, tool.SelectedVertexIndex);
        // Inherited speed from Wp0
        Assert.Equal(10f, tool.GhostWaypoints[1].TargetSpeed, precision: 3);
    }

    /// <summary>
    /// A left-click far from all vertices and segments must not change the ghost
    /// count or selection.
    /// </summary>
    [Fact]
    public void HandleClick_Left_FarFromAll_NoChangeToGhostOrSelection()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());

        tool.HandleClick(FarClick, MouseButton.Left);

        Assert.Equal(3, tool.GhostWaypoints.Count);
        Assert.Equal(-1, tool.SelectedVertexIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HandleClick — right-click commit
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A right-click must invoke the commit callback with the entity and a snapshot
    /// of the current ghost waypoints.
    /// </summary>
    [Fact]
    public void HandleClick_Right_InvokesCommitCallbackWithGhostCopy()
    {
        Entity committedEntity = default;
        List<RouteWaypoint>? committedWaypoints = null;
        var entity = new Entity(7, 0);

        var plan = MakeThreeWaypointPlan();
        var tool = new RouteEditTool(entity, plan, (e, wps) =>
        {
            committedEntity   = e;
            committedWaypoints = wps;
        });
        tool.OnEnter(null!);

        tool.HandleClick(Vector2.Zero, MouseButton.Right);

        Assert.Equal(entity, committedEntity);
        Assert.NotNull(committedWaypoints);
        Assert.Equal(3, committedWaypoints!.Count);
    }

    /// <summary>
    /// The committed list must be a fresh copy, not the same reference as the
    /// internal ghost list.
    /// </summary>
    [Fact]
    public void HandleClick_Right_CommittedListIsFreshCopy()
    {
        List<RouteWaypoint>? first  = null;
        List<RouteWaypoint>? second = null;

        var plan = MakeThreeWaypointPlan();
        var tool = new RouteEditTool(new Entity(1, 0), plan, (_, wps) =>
        {
            if (first == null) first   = wps;
            else               second  = wps;
        });
        tool.OnEnter(null!);

        // Right-click twice (simulating a second commit after re-entry).
        tool.HandleClick(Vector2.Zero, MouseButton.Right);
        tool.OnEnter(null!); // re-enter to reset ghost
        tool.HandleClick(Vector2.Zero, MouseButton.Right);

        Assert.NotSame(first, second);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HandleKeyPressed — delete
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pressing <c>Delete</c> when vertex 1 is selected must remove it from the
    /// ghost list.
    /// </summary>
    [Fact]
    public void HandleKeyPressed_Delete_RemovesSelectedVertex()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());

        tool.HandleClick(NearWp1, MouseButton.Left); // select vertex 1
        Assert.Equal(1, tool.SelectedVertexIndex);

        tool.HandleKeyPressed(KeyboardKey.Delete);

        Assert.Equal(2, tool.GhostWaypoints.Count);
        Assert.Equal(Wp0Pos, tool.GhostWaypoints[0].Position);
        Assert.Equal(Wp2Pos, tool.GhostWaypoints[1].Position);
    }

    /// <summary>
    /// Pressing <c>Delete</c> when no vertex is selected must do nothing.
    /// </summary>
    [Fact]
    public void HandleKeyPressed_Delete_NoSelection_GhostUnchanged()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());
        // SelectedVertexIndex starts at -1.

        tool.HandleKeyPressed(KeyboardKey.Delete);

        Assert.Equal(3, tool.GhostWaypoints.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HandleKeyPressed — escape
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pressing <c>Escape</c> must NOT invoke the commit callback.
    /// </summary>
    [Fact]
    public void HandleKeyPressed_Escape_DoesNotInvokeCommitCallback()
    {
        bool committed = false;
        var tool = new RouteEditTool(new Entity(1, 0), MakeThreeWaypointPlan(), (_, _) => committed = true);
        tool.OnEnter(null!);

        tool.HandleKeyPressed(KeyboardKey.Escape);

        Assert.False(committed, "Escape must discard edits without invoking the commit callback.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HandleDrag
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dragging when vertex 1 is selected must update its position by the delta.
    /// </summary>
    [Fact]
    public void HandleDrag_SelectedVertex_PositionUpdatedByDelta()
    {
        var tool  = CreateAndEnter(MakeThreeWaypointPlan());
        var delta = new Vector2(5f, -3f);

        tool.HandleClick(NearWp1, MouseButton.Left); // select vertex 1
        tool.HandleDrag(Canvas(Wp1Pos) + delta, delta);

        var expected = new Vector3(Wp1Pos.X + delta.X, 0f, Wp1Pos.Z + delta.Y);
        Assert.Equal(expected.X, tool.GhostWaypoints[1].Position.X, precision: 3);
        Assert.Equal(expected.Z, tool.GhostWaypoints[1].Position.Z, precision: 3);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GetSelectedWaypointRef — WaypointEditorPanel integration (T013)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When a vertex is selected, <see cref="RouteEditTool.GetSelectedWaypointRef"/>
    /// returns a mutable reference that allows in-place editing of
    /// <see cref="RouteWaypoint.TargetSpeed"/> — the primary operation of
    /// <c>WaypointEditorPanel</c> (ROUTES1-T013).
    /// </summary>
    [Fact]
    public void GetSelectedWaypointRef_WithSelection_AllowsInPlaceSpeedEdit()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());
        tool.HandleClick(NearWp1, MouseButton.Left); // select vertex 1

        ref var wp = ref tool.GetSelectedWaypointRef();
        wp.TargetSpeed = 99f;

        Assert.Equal(99f, tool.GhostWaypoints[1].TargetSpeed, precision: 3);
    }

    /// <summary>
    /// When a vertex is selected, <see cref="RouteEditTool.GetSelectedWaypointRef"/>
    /// returns a mutable reference that allows in-place editing of
    /// <see cref="RouteWaypoint.ExtensionJson"/> — used by <c>WaypointEditorPanel</c>
    /// (ROUTES1-T013).
    /// </summary>
    [Fact]
    public void GetSelectedWaypointRef_WithSelection_AllowsInPlaceJsonEdit()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());
        tool.HandleClick(NearWp1, MouseButton.Left); // select vertex 1

        ref var wp = ref tool.GetSelectedWaypointRef();
        wp.ExtensionJson = @"{""dangerLevel"":3}";

        Assert.Equal(@"{""dangerLevel"":3}", tool.GhostWaypoints[1].ExtensionJson);
    }

    /// <summary>
    /// When no vertex is selected (<see cref="RouteEditTool.SelectedVertexIndex"/> == −1),
    /// <see cref="RouteEditTool.GetSelectedWaypointRef"/> must throw
    /// <see cref="InvalidOperationException"/> — preventing WaypointEditorPanel from
    /// accessing invalid memory.
    /// </summary>
    [Fact]
    public void GetSelectedWaypointRef_NoSelection_ThrowsInvalidOperationException()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());
        // SelectedVertexIndex = -1 after OnEnter.

        Assert.Throws<InvalidOperationException>(() => tool.GetSelectedWaypointRef());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Vertex context menu — right-click behaviour
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Right-clicking within pick radius of a waypoint must set
    /// <see cref="RouteEditTool.PendingVertexContextMenu"/> and NOT commit.
    /// </summary>
    [Fact]
    public void RightClickOnVertex_SetsPendingVertexContextMenu_NoCommit()
    {
        int commitCount = 0;
        var tool = CreateAndEnter(MakeThreeWaypointPlan(),
            onCommit: (_, _) => commitCount++);

        tool.HandleClick(NearWp1, MouseButton.Right);

        Assert.True(tool.PendingVertexContextMenu);
        Assert.Equal(0, commitCount);
    }

    /// <summary>
    /// Right-clicking far from all waypoints must commit immediately.
    /// </summary>
    [Fact]
    public void RightClickFarFromVertices_CommitsImmediately()
    {
        int commitCount = 0;
        var tool = CreateAndEnter(MakeThreeWaypointPlan(),
            onCommit: (_, _) => commitCount++);

        tool.HandleClick(FarClick, MouseButton.Right);

        Assert.Equal(1, commitCount);
        Assert.False(tool.PendingVertexContextMenu);
    }

    /// <summary>
    /// <see cref="RouteEditTool.ContextMenuVertexIndex"/> must identify the right-clicked vertex.
    /// </summary>
    [Fact]
    public void RightClickOnVertex_SetsCorrectContextMenuVertexIndex()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());

        tool.HandleClick(NearWp1, MouseButton.Right);

        Assert.Equal(1, tool.ContextMenuVertexIndex);
    }

    // ── Insert waypoint ───────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="RouteEditTool.InsertWaypointAfterSelected"/> must add one waypoint at the
    /// midpoint between vertex[N] and vertex[N+1], inheriting speed, and clear the menu flag.
    /// </summary>
    [Fact]
    public void InsertWaypointAfterSelected_AddsWaypointAtMidpointWithInheritedSpeed()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());
        tool.HandleClick(NearWp1, MouseButton.Right); // ctx menu for waypoint 1

        int countBefore = tool.GhostWaypoints.Count;
        tool.InsertWaypointAfterSelected();

        Assert.Equal(countBefore + 1, tool.GhostWaypoints.Count);
        Assert.False(tool.PendingVertexContextMenu);

        // New waypoint inherits speed of waypoint 1.
        Assert.Equal(15f, tool.GhostWaypoints[2].TargetSpeed, precision: 4);
    }

    // ── Delete waypoint ───────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="RouteEditTool.DeleteSelectedWaypoint"/> must remove the waypoint and
    /// clear PendingVertexContextMenu.
    /// </summary>
    [Fact]
    public void DeleteSelectedWaypoint_RemovesWaypoint()
    {
        var tool = CreateAndEnter(MakeThreeWaypointPlan());
        tool.HandleClick(NearWp1, MouseButton.Right);

        tool.DeleteSelectedWaypoint();

        Assert.Equal(2, tool.GhostWaypoints.Count);
        Assert.False(tool.PendingVertexContextMenu);
    }

    /// <summary>
    /// Attempting to delete when only 2 waypoints remain must be a no-op
    /// (minimum viable route preserved).
    /// </summary>
    [Fact]
    public void DeleteSelectedWaypoint_WhenOnly2Remain_IsNoOp()
    {
        var twoWpPlan = new RoutePlan();
        twoWpPlan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = Wp0Pos });
            wps.Add(new RouteWaypoint { Position = Wp1Pos });
        });
        var tool = CreateAndEnter(twoWpPlan);

        // Right-click near Wp1 to open context menu.
        tool.HandleClick(NearWp1, MouseButton.Right);
        tool.DeleteSelectedWaypoint();

        Assert.Equal(2, tool.GhostWaypoints.Count);
        Assert.False(tool.PendingVertexContextMenu);
    }

    // ── Close context menu ────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="RouteEditTool.CloseVertexContextMenu"/> must clear
    /// <see cref="RouteEditTool.PendingVertexContextMenu"/> without committing.
    /// </summary>
    [Fact]
    public void CloseVertexContextMenu_ClearsPendingFlagWithoutCommit()
    {
        int commitCount = 0;
        var tool = CreateAndEnter(MakeThreeWaypointPlan(),
            onCommit: (_, _) => commitCount++);

        tool.HandleClick(NearWp1, MouseButton.Right);
        Assert.True(tool.PendingVertexContextMenu);

        tool.CloseVertexContextMenu();

        Assert.False(tool.PendingVertexContextMenu);
        Assert.Equal(0, commitCount);
    }
}
