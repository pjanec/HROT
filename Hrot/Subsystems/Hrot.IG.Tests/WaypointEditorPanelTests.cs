using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.IG.UI;
using Hrot.Map.Common.Components;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="WaypointEditorPanel"/> -- state-management logic (CT-2,
/// ROUTES1-BATCH-04).
///
/// <para>
/// Tests exercise <see cref="WaypointEditorPanel.UpdatePanelState"/> directly, which
/// contains the caching logic separated from the ImGui rendering calls. This allows
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
    // -- Test stub implementing IRouteWaypointEditorState --

    private sealed class StubRouteState : IRouteWaypointEditorState
    {
        private readonly RouteWaypoint[] _waypoints;

        public int SelectedVertexIndex { get; }

        public StubRouteState(RoutePlan plan, int selectedIndex)
        {
            var list = plan.Waypoints;
            _waypoints = list != null ? list.ToArray() : Array.Empty<RouteWaypoint>();
            SelectedVertexIndex = selectedIndex;
        }

        public ref RouteWaypoint GetSelectedWaypointRef()
        {
            var span = _waypoints.AsSpan();
            return ref span[SelectedVertexIndex];
        }
    }

    // -- Helpers --

    private static WaypointEditorPanel CreatePanel()
        => new WaypointEditorPanel(() => null);

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

    private static StubRouteState CreateStubState(RoutePlan plan, int selectIndex = 0)
        => new StubRouteState(plan, selectIndex);

    // -- InitialState --

    /// <summary>
    /// Directly after construction both <see cref="WaypointEditorPanel.TestHook_LastWpIndex"/>
    /// and <see cref="WaypointEditorPanel.TestHook_WasRouteToolActive"/> must be at their
    /// sentinel defaults (-1 and false respectively) before any <c>UpdatePanelState</c>
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

    // -- _lastWpIndex caching --

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
        var state = CreateStubState(MakePlan(@"{""dangerLevel"":1}", null));

        // First draw: index changes from -1 -> 0, buffer is populated.
        panel.UpdatePanelState(state);
        string firstRef = panel.TestHook_JsonBuffer;

        // Second draw: same index -- buffer must NOT be re-assigned.
        panel.UpdatePanelState(state);
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

        var stateAtWp0 = CreateStubState(plan, selectIndex: 0);
        panel.UpdatePanelState(stateAtWp0);

        Assert.Equal(0, panel.TestHook_LastWpIndex);
        Assert.Equal(@"{""dangerLevel"":1}", panel.TestHook_JsonBuffer);

        // Select wp1 (different stub state simulating a re-select).
        var stateAtWp1 = CreateStubState(plan, selectIndex: 1);
        panel.UpdatePanelState(stateAtWp1);

        Assert.Equal(1, panel.TestHook_LastWpIndex);
        Assert.Equal(@"{""dangerLevel"":99}", panel.TestHook_JsonBuffer);
    }

    // -- _wasRouteToolActive transitions --

    /// <summary>
    /// When <c>UpdatePanelState(null)</c> is called (gizmo deactivated / no selection),
    /// <see cref="WaypointEditorPanel.TestHook_WasRouteToolActive"/> must be
    /// <c>false</c> and <c>_lastWpIndex</c> must reset to -1.
    /// </summary>
    [Fact]
    public void WasRouteToolActive_TransitionsToFalse_WhenToolDeactivated()
    {
        var panel = CreatePanel();
        var state = CreateStubState(MakePlan("{}"));

        // Activate.
        panel.UpdatePanelState(state);
        Assert.True(panel.TestHook_WasRouteToolActive);
        Assert.Equal(0, panel.TestHook_LastWpIndex);

        // Deactivate (simulates gizmo disposal / marker removal).
        panel.UpdatePanelState(null);

        Assert.False(panel.TestHook_WasRouteToolActive);
        Assert.Equal(-1, panel.TestHook_LastWpIndex);
    }
}