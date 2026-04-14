using System;
using Hrot.ScenarioEditor.Tools;
using ImGuiNET;

namespace Hrot.IG.UI;

/// <summary>
/// ImGui panel that exposes per-waypoint editing controls when a
/// <see cref="RouteEditTool"/> is active and a vertex is selected (ROUTES1-T013).
///
/// <para>
/// The panel observes <see cref="RouteEditTool.SelectedVertexIndex"/> from the canvas
/// active tool each frame. When a vertex is selected, it renders:
/// <list type="bullet">
///   <item>Read-only position label.</item>
///   <item><c>Target Speed (m/s)</c> float input — updates
///         <see cref="Hrot.Map.Common.Components.RouteWaypoint.TargetSpeed"/> in-place.</item>
///   <item><c>AI Advice (JSON)</c> multiline text input — updates
///         <see cref="Hrot.Map.Common.Components.RouteWaypoint.ExtensionJson"/> in-place.</item>
/// </list>
/// </para>
///
/// <para>
/// The panel does NOT commit changes—<see cref="RouteEditTool"/> owns the ghost
/// state and emits the <c>UpdateEntityDescriptorRequest</c> on right-click commit.
/// </para>
/// </summary>
public class WaypointEditorPanel
{
    private readonly Fdp.Toolkit.Vis2D.MapCanvas _canvas;

    // Working buffer for the multiline JSON input (avoids per-frame allocation).
    private string _jsonBuffer = string.Empty;

    // CT-2: cache the last rendered waypoint index so we only copy ExtensionJson
    // into _jsonBuffer when the selection actually changes, avoiding per-frame
    // string allocation.
    private int _lastWpIndex = -1;

    // CT-2: tracks whether the route edit tool was active in the previous draw
    // call so that a right-click commit can be detected and keyboard focus cleared.
    private bool _wasRouteToolActive;

    // ── Test hooks ────────────────────────────────────────────────────────────

    /// <summary>Exposes the cached selection index for headless tests (CT-2).</summary>
    internal int    TestHook_LastWpIndex        => _lastWpIndex;

    /// <summary>Exposes the current JSON buffer contents for headless tests (CT-2).</summary>
    internal string TestHook_JsonBuffer         => _jsonBuffer;

    /// <summary>Exposes the focus-tracking state for headless tests (CT-2).</summary>
    internal bool   TestHook_WasRouteToolActive => _wasRouteToolActive;

    /// <param name="canvas">The map canvas whose <see cref="Fdp.Toolkit.Vis2D.MapCanvas.ActiveTool"/>
    /// is inspected each frame.</param>
    public WaypointEditorPanel(Fdp.Toolkit.Vis2D.MapCanvas canvas)
        => _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    /// <summary>
    /// Core panel state update: refreshes <see cref="_lastWpIndex"/>,
    /// <see cref="_jsonBuffer"/>, and <see cref="_wasRouteToolActive"/> based on the
    /// given tool reference. Separated from <see cref="Draw"/> so headless unit tests
    /// can exercise the caching logic without an active ImGui context (CT-2).
    /// </summary>
    /// <param name="activeRouteTool">
    /// The active <see cref="RouteEditTool"/> with a valid selection, or
    /// <see langword="null"/> when no vertex is selected.
    /// </param>
    internal void UpdatePanelState(RouteEditTool? activeRouteTool)
    {
        if (activeRouteTool == null)
        {
            _wasRouteToolActive = false;
            _lastWpIndex = -1;
            return;
        }

        _wasRouteToolActive = true;

        // Only refresh the JSON buffer when the selection index changes; avoids
        // per-frame string allocation for unchanged waypoints (CT-2).
        if (activeRouteTool.SelectedVertexIndex != _lastWpIndex)
        {
            _lastWpIndex = activeRouteTool.SelectedVertexIndex;
            ref var wp   = ref activeRouteTool.GetSelectedWaypointRef();
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
        var  routeTool    = _canvas.ActiveTool as RouteEditTool;
        bool hasSelection = routeTool?.SelectedVertexIndex >= 0;

        // CT-2: when a right-click commit completes the tool transitions away,
        // strip keyboard focus from any still-active ImGui input widget to prevent
        // stale float/text values from leaking into the next edit session.
        if (_wasRouteToolActive && !hasSelection)
            ImGui.SetKeyboardFocusHere(-1);

        UpdatePanelState(hasSelection ? routeTool : null);

        if (!hasSelection)
        {
            ImGui.TextDisabled("Select a waypoint to edit its properties.");
            return;
        }

        ref var wp = ref routeTool!.GetSelectedWaypointRef();

        // ── Position (read-only) ──────────────────────────────────────────────
        ImGui.LabelText("Position", $"({wp.Position.X:F1}, {wp.Position.Y:F1}, {wp.Position.Z:F1})");

        ImGui.Separator();

        // ── Target Speed ──────────────────────────────────────────────────────
        float speed = wp.TargetSpeed;
        if (ImGui.InputFloat("Target Speed (m/s)", ref speed))
            wp.TargetSpeed = System.Math.Max(0f, speed);

        // ── AI Advice JSON ────────────────────────────────────────────────────
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
