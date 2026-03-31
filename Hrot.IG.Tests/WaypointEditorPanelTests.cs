using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Tools;
using Hrot.IG.UI;
using Hrot.Map.Common.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="WaypointEditorPanel"/> — state-management logic (CT-2,
/// ROUTES1-BATCH-04).
///
/// <para>
/// Tests exercise <see cref="WaypointEditorPanel.UpdatePanelState"/> directly, which
/// contains the caching logic separated from the ImGui rendering calls.  This allows
/// headless execution without an active ImGui/Raylib context.
/// </para>
///
/// <para>
/// Assertions target the observable test-hook properties
/// (<c>TestHook_LastWpIndex</c>, <c>TestHook_JsonBuffer</c>,
/// <c>TestHook_WasRouteToolActive</c>) rather than ImGui widget state.
/// </para>
/// </summary>
public class WaypointEditorPanelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a headless <see cref="WaypointEditorPanel"/> backed by an
    /// empty <see cref="MapCanvas"/>.</summary>
    private static WaypointEditorPanel CreatePanel()
        => new WaypointEditorPanel(new MapCanvas());

    private static RoutePlan MakePlan(params string?[] jsonValues)
    {
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            for (int i = 0; i < jsonValues.Length; i++)
                wps.Add(new RouteWaypoint
                {
                    Position      = new Vector3(i * 10f, 0f, i * 10f),
                    TargetSpeed   = 5f,
                    ExtensionJson = jsonValues[i],
                });
        });
        return plan;
    }

    private static RouteEditTool CreateAndEnterTool(RoutePlan plan, int selectIndex = 0)
    {
        var tool = new RouteEditTool(new Entity(1, 0), plan, (_, _) => { });
        tool.OnEnter(null!);
        // Simulate a left-click near the waypoint to select it.
        var pos = new Vector2(plan.Waypoints[selectIndex].Position.X,
                              plan.Waypoints[selectIndex].Position.Z);
        tool.HandleClick(pos, Raylib_cs.MouseButton.Left);
        return tool;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Initial state
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Directly after construction both <see cref="WaypointEditorPanel.TestHook_LastWpIndex"/>
    /// and <see cref="WaypointEditorPanel.TestHook_WasRouteToolActive"/> must be at their
    /// sentinel defaults (−1 and false respectively) before any <c>UpdatePanelState</c>
    /// call.
    /// </summary>
    [Fact]
    public void InitialState_LastWpIndexMinusOne_WasRouteToolActiveFalse()
    {
        var panel = CreatePanel();

        Assert.Equal(-1, panel.TestHook_LastWpIndex);
        Assert.False(panel.TestHook_WasRouteToolActive);
        Assert.Equal(string.Empty, panel.TestHook_JsonBuffer);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // _lastWpIndex caching — buffer allocation behaviour
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When <c>UpdatePanelState</c> is called twice for the same selection, the
    /// <c>_jsonBuffer</c> string reference must remain identical (no new string
    /// created), validating structural continuity across unaffected layout draws
    /// (CT-2 memory layout check).
    /// </summary>
    [Fact]
    public void JsonBuffer_NotUpdatedWhenWaypointIndexUnchanged_SameReference()
    {
        var panel = CreatePanel();
        var tool  = CreateAndEnterTool(MakePlan(@"{""dangerLevel"":1}", null));

        // First draw: index changes from -1 → 0, buffer is populated.
        panel.UpdatePanelState(tool);
        string firstRef = panel.TestHook_JsonBuffer;

        // Second draw: same index — buffer must NOT be re-assigned.
        panel.UpdatePanelState(tool);
        string secondRef = panel.TestHook_JsonBuffer;

        Assert.Equal(0, panel.TestHook_LastWpIndex);
        Assert.True(ReferenceEquals(firstRef, secondRef),
            "JsonBuffer must not be re-assigned when the selected waypoint index is unchanged.");
    }

    /// <summary>
    /// When the selection moves to a different waypoint, <c>_jsonBuffer</c> must be
    /// refreshed with the new waypoint's <see cref="RouteWaypoint.ExtensionJson"/>
    /// and <c>_lastWpIndex</c> must reflect the new index.
    /// </summary>
    [Fact]
    public void JsonBuffer_UpdatedWhenWaypointIndexChanges_ReflectsNewJson()
    {
        var panel = CreatePanel();
        var plan  = MakePlan(@"{""dangerLevel"":1}", @"{""dangerLevel"":99}");

        var toolAtWp0 = CreateAndEnterTool(plan, selectIndex: 0);
        panel.UpdatePanelState(toolAtWp0);

        Assert.Equal(0, panel.TestHook_LastWpIndex);
        Assert.Equal(@"{""dangerLevel"":1}", panel.TestHook_JsonBuffer);

        // Select wp1 (different tool instance simulating a re-select).
        var toolAtWp1 = CreateAndEnterTool(plan, selectIndex: 1);
        panel.UpdatePanelState(toolAtWp1);

        Assert.Equal(1, panel.TestHook_LastWpIndex);
        Assert.Equal(@"{""dangerLevel"":99}", panel.TestHook_JsonBuffer);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // _wasRouteToolActive transitions (CT-2 focus guard)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When <c>UpdatePanelState(null)</c> is called (tool deactivated / no selection),
    /// <see cref="WaypointEditorPanel.TestHook_WasRouteToolActive"/> must be
    /// <c>false</c> and <c>_lastWpIndex</c> must reset to −1.
    /// </summary>
    [Fact]
    public void WasRouteToolActive_TransitionsToFalse_WhenToolDeactivated()
    {
        var panel = CreatePanel();
        var tool  = CreateAndEnterTool(MakePlan("{}"));

        // Activate.
        panel.UpdatePanelState(tool);
        Assert.True(panel.TestHook_WasRouteToolActive);
        Assert.Equal(0, panel.TestHook_LastWpIndex);

        // Deactivate (simulates right-click commit that pops the tool).
        panel.UpdatePanelState(null);

        Assert.False(panel.TestHook_WasRouteToolActive);
        Assert.Equal(-1, panel.TestHook_LastWpIndex);
    }
}
