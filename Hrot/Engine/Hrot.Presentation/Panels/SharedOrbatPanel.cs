using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using ImGuiNET;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Panels;

/// <summary>⭐⭐⭐ U-obs-5 — the whole of what <see cref="SharedOrbatPanel"/> shows, this frame. ⚠ See
/// <c>ConfigPanel</c>'s remarks for the group-5 twin finding — ironically this is the very panel
/// <c>docs/projects/Hrot/Engine/Hrot.UI.Common.md</c> names as the example of the intended sharing that
/// never actually happened at the assembly level. ⭐ <see cref="Nodes"/> is dumped as-is — already a
/// flat, delegate-free DTO.</summary>
public sealed record SharedOrbatPanelViewModel(
    string PanelId, string PanelKind, string FilterText,
    IReadOnlyList<int> ExpandedNodeIds, IReadOnlyList<OrbatNodeViewModel> Nodes,
    string Relation = "Command") : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

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
    /// <summary>⭐ <c>CE-1230</c> (canvas context menu §11 OM-1, R-1019) — the map's entity menu, shown on a row's right-click; set by a host
    /// with a map (its inspector state finds it, as the entity inspector's menu does). Null ⇒ no row menu (OM-5).</summary>
    public Hrot.UI.Common.Menus.MapEntityMenuHandler? RowMenu { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private string _filterText = string.Empty;

    /// <summary>⭐ CE-4213 (DESIGN_Entity_Hierarchy_Views HV-2) — the relation this panel shows; applied to the provider every frame.
    /// <see cref="EntityTreeRelation.Scenario"/> (⭐ CE-4213 ⑥) shows the macro instance each entity came from, roots under Saved / Transient — read-only.</summary>
    public EntityTreeRelation Relation { get; set; } = EntityTreeRelation.Command;

    /// <summary>⭐ CE-4213 ⑥ — the colour of a refused drop's reason (amber: it is a "no, because", not an error).</summary>
    public static readonly System.Numerics.Vector4 RefusalColour = new(0.95f, 0.70f, 0.20f, 1f);

    /// <summary>⭐ <c>CE-1350</c> — a destroyed unit's row text (dark red, still readable on the dark theme).</summary>
    public static readonly System.Numerics.Vector4 DestroyedColour = new(0.72f, 0.16f, 0.14f, 1f);
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

    /// <summary>Expands a node (a host restoring the tree's state; the frame rail's seam). The arrow button toggles the same set.</summary>
    public void Expand(int entityId) => _expandedNodes.Add(entityId);

    // ── ⭐ CE-4213 — what the last <see cref="DrawContent"/> actually EMITTED (the frame rail asserts on it; not a model projection) ──

    /// <summary>One row as drawn: its text, depth, whether it was a group label or a selectable entity row, and the arrow it carried (null = none, else expanded or not).</summary>
    public readonly record struct DrawnRow(string Text, int Depth, bool IsGroup, bool? ArrowExpanded, System.Numerics.Vector2 Min = default, System.Numerics.Vector2 Max = default);

    /// <summary>The relation switch's three radio buttons as laid out: label and screen rectangle (what a click lands on).</summary>
    public readonly record struct DrawnSwitch(string Label, System.Numerics.Vector2 Min, System.Numerics.Vector2 Max);

    private readonly List<DrawnRow> _drawn = new();
    private readonly List<DrawnSwitch> _switch = new();

    /// <summary>The rows the last frame drew, top to bottom.</summary>
    public IReadOnlyList<DrawnRow> DrawnRows => _drawn;

    /// <summary>The relation switch the last frame drew.</summary>
    public IReadOnlyList<DrawnSwitch> RelationSwitch => _switch;

    /// <summary>The refusal line the last frame drew, or null.</summary>
    public string? DrawnRefusal { get; private set; }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure projection of the visible tree. No ImGui. ⭐ Calls
    /// <c>data.GetVisibleNodes</c>, the SAME source <see cref="DrawContent"/> reads.</summary>
    public SharedOrbatPanelViewModel BuildViewModel(IOrbatDataProvider data, string panelId, string panelKind)
    {
        data.Relation = Relation;
        var nodes = data.GetVisibleNodes(_filterText, _expandedNodes);
        return new SharedOrbatPanelViewModel(panelId, panelKind, _filterText, _expandedNodes.ToList(), nodes, Relation.ToString());
    }

    // ── Render ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the ORBAT panel.  Must be called inside an active ImGui window.
    /// </summary>
    public void DrawContent(IOrbatDataProvider data, IOrbatController ctrl)
    {
        _drawn.Clear(); _switch.Clear(); DrawnRefusal = null;
        // ⭐ CE-4213 — the relation switch: ONE tree, the parent rule chosen here.
        if (ImGui.RadioButton("Command", Relation == EntityTreeRelation.Command)) Relation = EntityTreeRelation.Command;
        _switch.Add(new("Command", ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
        ImGui.SameLine();
        if (ImGui.RadioButton("Structure", Relation == EntityTreeRelation.Structure)) Relation = EntityTreeRelation.Structure;
        _switch.Add(new("Structure", ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
        ImGui.SameLine();
        if (ImGui.RadioButton("Scenario", Relation == EntityTreeRelation.Scenario)) Relation = EntityTreeRelation.Scenario;
        _switch.Add(new("Scenario", ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
        data.Relation = Relation;
        bool editable = Relation == EntityTreeRelation.Command;   // Structure and Scenario: no drag, no drop targets

        // ⭐ CE-4213 ⑥ — why the last drop was refused (a macro entity cannot be moved in the chain of command), where the user is looking
        if (Relation == EntityTreeRelation.Command && data.LastDropRefusal is { Length: > 0 } refusal)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, RefusalColour);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(refusal);
            DrawnRefusal = refusal;
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }

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
            bool? arrow = null;
            if (node.HasChildren)
            {
                bool expanded = _expandedNodes.Contains(node.EntityId);
                arrow = expanded;
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

            // ⭐ CE-4213 ⑥ — a Scenario-view GROUP row (Saved / Transient) has no entity: a plain label, no select, drag, drop or menu
            if (node.IsGroup)
            {
                ImGui.TextUnformatted(node.Name);
                _drawn.Add(new(node.Name, node.Depth, true, arrow, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
                if (node.Depth > 0) ImGui.Unindent(node.Depth * 12f);
                continue;
            }

            // Selectable row — ⭐ CE-1350: a destroyed unit's name in dark red; a double-click centres the map on it (Center View)
            if (node.IsDestroyed) ImGui.PushStyleColor(ImGuiCol.Text, DestroyedColour);
            bool picked = ImGui.Selectable($"{node.Name}##{node.EntityId}", false, ImGuiSelectableFlags.AllowDoubleClick);
            _drawn.Add(new(node.Name, node.Depth, false, arrow, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
            if (picked)
            {
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) ctrl.CenterOn(node.EntityId);
                else ctrl.SelectEntity(node.EntityId);
            }
            if (node.IsDestroyed)
            {
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Destroyed");
            }

            // Drag source — unsafe pointer operations confined here
            unsafe
            {
                if (editable && ImGui.BeginDragDropSource())
                {
                    int id = node.EntityId;
                    ImGui.SetDragDropPayload("ORBAT_ENTITY", (nint)(&id), 4);
                    ImGui.EndDragDropSource();
                }
            }

            // Drop target
            if (editable && ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var payload = ImGui.AcceptDragDropPayload("ORBAT_ENTITY");
                    if (payload.NativePtr != null)
                    {
                        int passengerId = *(int*)payload.Data;
                        int vehicleId   = node.EntityId;
                        HandleHierarchyDropPayload(passengerId, node, ctrl);
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // ⭐ CE-1230 (canvas context menu §11 OM-1, OM-4, R-1019) — the row's right-click menu IS the map's entity menu (the inspector's
            //   route); the panel's own one-item "Disembark" popup is gone (Disembark / Embark… are in the shared menu). A host with no map
            //   (ExCon) has no row menu (OM-5).
            if (RowMenu is { } rowMenu && ImGui.BeginPopupContextItem($"##ctx_{node.EntityId}"))
            {
                rowMenu.PopulateMenuFor(node.NetworkId, new Fdp.Presentation.Utils.ContextMenuBuilder());   // ⭐ CE-1271: the network id, not the row key
                ImGui.EndPopup();
            }

            if (node.Depth > 0)
                ImGui.Unindent(node.Depth * 12f);
        }

        // Background drop target — dropping here removes the entity from its current command hierarchy.
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, Math.Max(ImGui.GetContentRegionAvail().Y, 20f)));
        if (editable && ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var bgPayload = ImGui.AcceptDragDropPayload("ORBAT_ENTITY");
                if (bgPayload.NativePtr != null)
                {
                    int subordinateId = *(int*)bgPayload.Data;
                    ctrl.RequestRemoveSubordinate(subordinateId);
                }
            }
            ImGui.EndDragDropTarget();
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
    /// Resolves a hierarchy-aware drop onto a node.
    /// If the target node <see cref="OrbatNodeViewModel.CanAcceptSubordinates"/>, calls
    /// <see cref="IOrbatController.RequestAssignSubordinate"/>; otherwise falls back to
    /// <see cref="IOrbatController.RequestEmbark"/>.
    /// Self-drops are always ignored.
    /// </summary>
    internal void HandleHierarchyDropPayload(int subId, OrbatNodeViewModel targetNode, IOrbatController ctrl)
    {
        if (subId == targetNode.EntityId)
            return;

        if (targetNode.CanAcceptSubordinates)
            ctrl.RequestAssignSubordinate(subId, targetNode.EntityId);
        else
            ctrl.RequestEmbark(subId, targetNode.EntityId);
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
