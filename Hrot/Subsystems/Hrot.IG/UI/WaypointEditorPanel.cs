using System;
using Hrot.ScenarioEditor.Gizmos;
using ImGuiNET;

namespace Hrot.IG.UI;

/// <summary>
/// ImGui panel that exposes per-waypoint editing controls when a
/// <see cref="RouteWaypointGizmo"/> is active and a vertex is selected (ROUTES1-T013).
///
/// <para>
/// The panel reads <see cref="IRouteWaypointEditorState.SelectedVertexIndex"/> from the
/// active gizmo state each frame. When a vertex is selected, it renders:
/// <list type="bullet">
///   <item>Read-only position label.</item>
///   <item><c>Target Speed (m/s)</c> float input -- updates
///         <see cref="Hrot.Map.Common.Components.RouteWaypoint.TargetSpeed"/> in-place.</item>
///   <item><c>AI Advice (JSON)</c> multiline text input -- updates
///         <see cref="Hrot.Map.Common.Components.RouteWaypoint.ExtensionJson"/> in-place.</item>
/// </list>
/// </para>
///
/// <para>
/// The panel does NOT commit changes -- <see cref="RouteWaypointGizmo"/> owns the working
/// state and writes back on each <c>OnCommit</c>.
/// </para>
/// </summary>
public class WaypointEditorPanel
{
    private readonly Func<IRouteWaypointEditorState?> _getActiveState;

    // Working buffer for the multiline JSON input (avoids per-frame allocation).
    private string _jsonBuffer = string.Empty;

    // CT-2: cache the last rendered waypoint index so we only copy ExtensionJson
    // into _jsonBuffer when the selection actually changes, avoiding per-frame
    // string allocation.
    private int _lastWpIndex = -1;

    // CT-2: tracks whether the gizmo was active in the previous draw call so that
    // a deactivation can be detected and keyboard focus cleared.
    private bool _wasRouteToolActive;

    // -- Test hooks --

    /// <summary>Exposes the cached selection index for headless tests (CT-2).</summary>
    internal int    TestHook_LastWpIndex        => _lastWpIndex;

    /// <summary>Exposes the current JSON buffer contents for headless tests (CT-2).</summary>
    internal string TestHook_JsonBuffer         => _jsonBuffer;

    /// <summary>Exposes the focus-tracking state for headless tests (CT-2).</summary>
    internal bool   TestHook_WasRouteToolActive => _wasRouteToolActive;

    /// <param name="getActiveState">
    /// Factory that returns the currently active <see cref="IRouteWaypointEditorState"/>,
    /// or <see langword="null"/> when no route editing gizmo is active.
    /// </param>
    public WaypointEditorPanel(Func<IRouteWaypointEditorState?> getActiveState)
        => _getActiveState = getActiveState ?? throw new ArgumentNullException(nameof(getActiveState));

    /// <summary>
    /// Core panel state update: refreshes <see cref="_lastWpIndex"/>,
    /// <see cref="_jsonBuffer"/>, and <see cref="_wasRouteToolActive"/> based on the
    /// given gizmo state. Separated from <see cref="Draw"/> so headless unit tests
    /// can exercise the caching logic without an active ImGui context (CT-2).
    /// </summary>
    /// <param name="activeState">
    /// The active <see cref="IRouteWaypointEditorState"/> with a valid selection, or
    /// <see langword="null"/> when no vertex is selected.
    /// </param>
    internal void UpdatePanelState(IRouteWaypointEditorState? activeState)
    {
        if (activeState == null)
        {
            _wasRouteToolActive = false;
            _lastWpIndex = -1;
            return;
        }

        _wasRouteToolActive = true;

        // Only refresh the JSON buffer when the selection index changes; avoids
        // per-frame string allocation for unchanged waypoints (CT-2).
        if (activeState.SelectedVertexIndex != _lastWpIndex)
        {
            _lastWpIndex = activeState.SelectedVertexIndex;
            ref var wp   = ref activeState.GetSelectedWaypointRef();
            _jsonBuffer  = wp.ExtensionJson ?? string.Empty;
        }
    }

    /// <summary>
    /// Renders the waypoint editor ImGui window.
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        IgPanelColors.Push();
        bool visible = ImGui.Begin("Waypoint Editor");
        IgPanelColors.Pop();
        if (!visible) { ImGui.End(); return; }
        DrawContent();
        ImGui.End();
    }

    /// <summary>
    /// Renders the panel content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent()
    {
        var activeState = _getActiveState();
        bool hasSelection = activeState?.SelectedVertexIndex >= 0;

        // CT-2: when the gizmo deactivates, strip keyboard focus from any
        // still-active ImGui input widget to prevent stale values from leaking.
        if (_wasRouteToolActive && !hasSelection)
            ImGui.SetKeyboardFocusHere(-1);

        UpdatePanelState(hasSelection ? activeState : null);

        if (!hasSelection)
        {
            ImGui.TextDisabled("Select a waypoint to edit its properties.");
            return;
        }

        ref var wp = ref activeState!.GetSelectedWaypointRef();

        // -- Position (read-only) --
        ImGui.LabelText("Position", $"({wp.Position.X:F1}, {wp.Position.Y:F1}, {wp.Position.Z:F1})");

        ImGui.Separator();

        // -- Target Speed --
        float speed = wp.TargetSpeed;
        if (ImGui.InputFloat("Target Speed (m/s)", ref speed))
            wp.TargetSpeed = System.Math.Max(0f, speed);

        // -- AI Advice JSON --
        if (ImGui.InputTextMultiline(
                "AI Advice (JSON)",
                ref _jsonBuffer,
                maxLength: 2048,
                size: new System.Numerics.Vector2(0f, 80f)))
        {
            wp.ExtensionJson = string.IsNullOrWhiteSpace(_jsonBuffer) ? null : _jsonBuffer;
        }
    }
}