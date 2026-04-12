using ImGuiNET;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// Shared ORBAT (Order of Battle) tree panel.
///
/// <para>Renders a hierarchical entity list with depth-based indentation,
/// text filtering, click-to-select, ImGui drag-and-drop embarkation, and a
/// right-click "Disembark" context menu.</para>
///
/// <para><b>Drag-and-drop:</b> each node is simultaneously a drag source
/// (payload type <c>"ORBAT_ENTITY"</c>, carries a 4-byte entity ID) and
/// a drop target.  Dropping a node onto a <em>different</em> node invokes
/// <see cref="IOrbatController.RequestEmbark"/>.
/// Dropping a node onto itself is a no-op.</para>
///
/// <para><b>Testing:</b> drag-drop resolution logic is exposed via the
/// <c>internal</c> method <see cref="HandleDropPayload"/> so tests can
/// exercise it without an ImGui render frame.</para>
/// </summary>
public sealed class SharedOrbatPanel
{
    // ── State ─────────────────────────────────────────────────────────────────

    private string _filterText = string.Empty;
    private readonly HashSet<int> _expandedNodes = new();

    // ── Public accessors (test helpers) ───────────────────────────────────────

    /// <summary>Current text filter applied to <c>GetVisibleNodes</c>.</summary>
    public string FilterText
    {
        get => _filterText;
        set => _filterText = value ?? string.Empty;
    }

    /// <summary>Set of expanded node IDs managed by this panel instance.</summary>
    public IReadOnlySet<int> ExpandedNodes => _expandedNodes;

    // ── Render ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the ORBAT panel.  Must be called inside an active ImGui window.
    /// </summary>
    public void DrawContent(IOrbatDataProvider data, IOrbatController ctrl)
    {
        // Filter text box
        ImGui.InputText("##orbat_filter", ref _filterText, 128);
        ImGui.SameLine();
        ImGui.TextUnformatted("Filter");

        ImGui.Separator();

        var nodes = data.GetVisibleNodes(_filterText, _expandedNodes);

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            if (node.Depth > 0)
                ImGui.Indent(node.Depth * 12f);

            // Arrow toggle for nodes with children
            if (node.HasChildren)
            {
                bool expanded = _expandedNodes.Contains(node.EntityId);
                if (ImGui.ArrowButton($"##arr_{node.EntityId}", expanded ? ImGuiDir.Down : ImGuiDir.Right))
                {
                    if (expanded)
                        _expandedNodes.Remove(node.EntityId);
                    else
                        _expandedNodes.Add(node.EntityId);
                    ctrl.ToggleExpanded(node.EntityId);
                }
                ImGui.SameLine();
            }

            // Selectable row
            if (ImGui.Selectable($"{node.Name}##{node.EntityId}", false))
                ctrl.SelectEntity(node.EntityId);

            // Drag source — unsafe pointer operations confined here
            unsafe
            {
                if (ImGui.BeginDragDropSource())
                {
                    int id = node.EntityId;
                    ImGui.SetDragDropPayload("ORBAT_ENTITY", (nint)(&id), 4);
                    ImGui.EndDragDropSource();
                }
            }

            // Drop target
            if (ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var payload = ImGui.AcceptDragDropPayload("ORBAT_ENTITY");
                    if (payload.NativePtr != null)
                    {
                        int passengerId = *(int*)payload.Data;
                        int vehicleId   = node.EntityId;
                        HandleDropPayload(passengerId, vehicleId, ctrl);
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // Right-click context menu — "Disembark" item
            if (ImGui.BeginPopupContextItem($"##ctx_{node.EntityId}"))
            {
                if (ImGui.MenuItem("Disembark"))
                    ctrl.RequestDisembark(node.EntityId);
                ImGui.EndPopup();
            }

            if (node.Depth > 0)
                ImGui.Unindent(node.Depth * 12f);
        }
    }

    // ── Internal logic (exposed for unit testing) ─────────────────────────────

    /// <summary>
    /// Resolves a received drop payload.
    /// Calls <see cref="IOrbatController.RequestEmbark"/> only when
    /// <paramref name="passengerId"/> differs from <paramref name="vehicleId"/>.
    /// </summary>
    internal void HandleDropPayload(int passengerId, int vehicleId, IOrbatController ctrl)
    {
        if (passengerId != vehicleId)
            ctrl.RequestEmbark(passengerId, vehicleId);
    }

    /// <summary>
    /// Simulates a selection click for the node with the given entity ID.
    /// Used by unit tests to verify controller callbacks without ImGui.
    /// </summary>
    internal void HandleSelectEntity(int entityId, IOrbatController ctrl)
    {
        ctrl.SelectEntity(entityId);
    }
}
