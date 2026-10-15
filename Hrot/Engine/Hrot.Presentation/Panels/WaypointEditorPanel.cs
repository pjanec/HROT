using System;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Navigation;
using Hrot.ScenarioEditor.Gizmos;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>⭐⭐⭐ U-obs-5 (group 6) — the whole of what <see cref="WaypointEditorPanel"/> shows, this
/// frame. ⚠ A plain panel: no <see cref="PanelId"/>/<see cref="PanelKind"/> of its own — the HOST
/// (<see cref="Hrot.Presentation.Windows.WaypointEditorWindow"/>) supplies both. ⭐ CE-1096 — and the waypoint's air fields.</summary>
public sealed record WaypointEditorPanelViewModel(
    string PanelId, string PanelKind, bool HasSelection, int SelectedVertexIndex,
    float PositionX, float PositionY, float PositionZ, float TargetSpeed, string ExtensionJson,
    string Kind = "Pass", float HeightAgl = 0f, string Hold = "None", float HoldSeconds = 0f, long HoldUnit = 0L) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

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
///   <item>⭐ CE-1096 (docs/designs/routes-1/ROUTES1-DESIGN.md §11.4 W-2 — air hand-off U2) — the air fields: <c>Kind</c> (Pass /
///         Ground), <c>Height above ground (m)</c> (0 = the stored height), <c>Hold</c> (None / Seconds / Unload / Pickup), its seconds and
///         its unit's network id.</item>
/// </list>
/// </para>
///
/// <para>
/// ⭐ CE-1096 (W-3) — a field the author changes is COMMITTED at once (<see cref="IRouteWaypointEditorState.CommitEdits"/>): the panel used to
/// edit the gizmo's working copy only, so an edit was lost on Escape.
/// </para>
///
/// <para>⭐ CE-1096 (W-1) — MOVED from <c>Hrot.IG.UI</c> to <c>Hrot.Presentation</c>: one panel, registered by every authoring host (the editor,
/// CGF, the IG — <see cref="Hrot.Presentation.Windows.WaypointEditorWindow"/>); the editor had no way to set anything on a waypoint.</para>
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
    /// given gizmo state. Separated from <see cref="DrawContent"/> so headless unit tests
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

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the active gizmo's selected waypoint, if any. No
    /// ImGui. ⭐ Reuses <see cref="UpdatePanelState"/>, the SAME cache refresh <see cref="DrawContent"/>
    /// uses, so the model can never drift from what is drawn.</summary>
    public WaypointEditorPanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        var activeState = _getActiveState();
        bool hasSelection = activeState?.SelectedVertexIndex >= 0;
        UpdatePanelState(hasSelection ? activeState : null);

        if (!hasSelection)
            return new(panelId, panelKind, false, -1, 0f, 0f, 0f, 0f, string.Empty);

        ref var wp = ref activeState!.GetSelectedWaypointRef();
        return new(panelId, panelKind, true, activeState.SelectedVertexIndex,
            wp.Position.X, wp.Position.Y, wp.Position.Z, wp.TargetSpeed, wp.ExtensionJson ?? string.Empty,
            wp.Kind.ToString(), wp.HeightAgl, wp.Hold.ToString(), wp.HoldSeconds, wp.HoldUnit);
    }

    /// <summary>
    /// Renders the waypoint editor as its own ImGui window (a host that does not manage its panels as windows — the IG standalone).
    /// <paramref name="beforeBegin"/> / <paramref name="afterBegin"/> let the host style the window (the IG's colours) around
    /// <c>ImGui.Begin</c>.
    /// </summary>
    public void Draw(Action? beforeBegin = null, Action? afterBegin = null)
    {
        beforeBegin?.Invoke();
        bool visible = ImGui.Begin("Waypoint Editor");
        afterBegin?.Invoke();
        if (!visible) { ImGui.End(); return; }
        DrawContent();
        ImGui.End();
    }

    /// <summary>⭐ CE-1096 (W-2) — the kinds and holds the combos offer, in their enum order.</summary>
    private static readonly string[] KindNames = Enum.GetNames<RouteWaypointKind>();
    private static readonly string[] HoldNames = Enum.GetNames<RouteHold>();

    /// <summary>
    /// Renders the panel content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="Fdp.Presentation.WindowManager.ManagedWindow"/>'s <c>DrawClientArea</c>.
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
        bool changed = false;

        // -- Position (read-only) --
        ImGui.LabelText("Position", $"({wp.Position.X:F1}, {wp.Position.Y:F1}, {wp.Position.Z:F1})");

        ImGui.Separator();

        // -- Target Speed --
        float speed = wp.TargetSpeed;
        if (ImGui.InputFloat("Target Speed (m/s)", ref speed))
            wp.TargetSpeed = System.Math.Max(0f, speed);
        changed |= ImGui.IsItemDeactivatedAfterEdit();

        // -- ⭐ CE-1096 (W-2) — the air fields --
        int kind = (int)wp.Kind;
        if (ImGui.Combo("Kind", ref kind, KindNames, KindNames.Length)) { wp.Kind = (RouteWaypointKind)kind; changed = true; }
        float height = wp.HeightAgl;
        if (ImGui.InputFloat("Height above ground (m)", ref height)) wp.HeightAgl = System.Math.Max(0f, height);
        changed |= ImGui.IsItemDeactivatedAfterEdit();
        if (wp.HeightAgl <= 0f) ImGui.TextDisabled("0 = the waypoint's own height");
        int hold = (int)wp.Hold;
        if (ImGui.Combo("Hold", ref hold, HoldNames, HoldNames.Length)) { wp.Hold = (RouteHold)hold; changed = true; }
        if (wp.Hold == RouteHold.Seconds)
        {
            float seconds = wp.HoldSeconds;
            if (ImGui.InputFloat("Hold (s)", ref seconds)) wp.HoldSeconds = System.Math.Max(0f, seconds);
            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }
        if (wp.Hold == RouteHold.Pickup)
        {
            string unit = wp.HoldUnit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (ImGui.InputText("Unit to pick up (network id)", ref unit, 24)
                && long.TryParse(unit, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id))
                wp.HoldUnit = id;
            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }

        ImGui.Separator();

        // -- AI Advice JSON --
        if (ImGui.InputTextMultiline(
                "AI Advice (JSON)",
                ref _jsonBuffer,
                maxLength: 2048,
                size: new System.Numerics.Vector2(0f, 80f)))
        {
            wp.ExtensionJson = string.IsNullOrWhiteSpace(_jsonBuffer) ? null : _jsonBuffer;
        }
        changed |= ImGui.IsItemDeactivatedAfterEdit();

        // ⭐ CE-1096 (W-3) — committed when made, not lost on Escape
        if (changed) activeState.CommitEdits();
    }
}
