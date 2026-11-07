using Hrot.Common;
using ImGuiNET;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;

namespace Hrot.Editor.UI;

/// <summary>⭐⭐⭐ U-obs-5 (group 6) — the whole of what <see cref="EditorToolbarPanel"/> shows, this
/// frame. ⚠ A plain panel: no <see cref="PanelId"/>/<see cref="PanelKind"/> of its own — the HOST
/// (<c>EditorToolbarWindow</c>) supplies both. ⚠ Static chrome since the Go External toggle — its only state, the
/// editor's <c>CurrentMode</c> — was retired (R-204, CE-3058).</summary>
public sealed record EditorToolbarPanelViewModel(
    string PanelId, string PanelKind, string? ActiveTool = null) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Editor toolbar panel for tool mode selection.
/// Delegates all tool activation to <see cref="IEditorLogic"/>.
/// </summary>
public sealed class EditorToolbarPanel
{
    // ⭐ B-17 P1 (DESIGN_Map_Interaction §2.5 "the toolbar marks it", Tool Model step 5 / CE-259m) — the armed tool's id (ToolController.ActiveModal)
    private readonly System.Func<string?> _activeTool;

    /// <param name="activeTool">The map's armed tool id (<c>ToolController.ActiveModal?.Id</c>); 🔒 the editor passes it (silent-default rule) —
    /// null only for a test that draws no state.</param>
    public EditorToolbarPanel(System.Func<string?>? activeTool = null) => _activeTool = activeTool ?? (() => null);

    /// <summary>The tool the toolbar marks as armed now (its id), or null.</summary>
    public string? ActiveTool => _activeTool();

    /// <summary>The style colour of an armed tool's button.</summary>
    public static readonly Vector4 ArmedColour = new(0.20f, 0.55f, 0.50f, 1f);
    // ── Testable handlers ─────────────────────────────────────────────────────

    public void HandleSpawnClick(IEditorLogic logic)  => logic.ActivateTool(EditorTool.Spawn);
    public void HandleSelectClick(IEditorLogic logic) => logic.ActivateTool(EditorTool.Select);
    public void HandleEditClick(IEditorLogic logic)   => logic.ActivateTool(EditorTool.Edit);
    public void HandleRouteClick(IEditorLogic logic)  => logic.ActivateTool(EditorTool.Route);

    public void HandleReloadAIClick(IEditorLogic logic) => logic.RebuildAndReloadAI();

    /// <summary>⭐⭐⭐ BUILD — the panel's snapshot. No ImGui.</summary>
    public EditorToolbarPanelViewModel BuildViewModel(IEditorLogic logic, string panelId, string panelKind) =>
        new(panelId, panelKind, ActiveTool);

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        ImGui.Text("Tools");
        ImGui.Separator();
        // ⭐ B-17 P1 — the armed tool's button is lit (no tool armed = Select)
        string active = ActiveTool ?? Hrot.ScenarioEditor.Tools.ScenarioToolIds.Select;
        if (ToolButton("Select", EditorTool.Select, active))       HandleSelectClick(logic);
        ImGui.SameLine();
        if (ToolButton("Place Entity", EditorTool.Spawn, active)) HandleSpawnClick(logic);
        ImGui.SameLine();
        if (ToolButton("Edit Shape", EditorTool.Edit, active))     HandleEditClick(logic);
        ImGui.SameLine();
        if (ToolButton("Edit Route", EditorTool.Route, active))    HandleRouteClick(logic);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiNET.ImGuiCol.Button, new Vector4(0.8f, 0.4f, 0.0f, 1.0f));
        if (ImGui.Button("Reload BTrees")) HandleReloadAIClick(logic);
        ImGui.PopStyleColor();
    }

    /// <summary>One tool button, lit when its tool is the armed one.</summary>
    private static bool ToolButton(string label, EditorTool tool, string active)
    {
        bool armed = Hrot.ScenarioEditor.Tools.ScenarioToolIds.ForEditorTool(tool) == active;
        if (armed) ImGui.PushStyleColor(ImGuiCol.Button, ArmedColour);
        bool clicked = ImGui.Button(label);
        if (armed) ImGui.PopStyleColor();
        return clicked;
    }
}
