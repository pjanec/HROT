using System;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using ImGuiNET;

namespace Hrot.IG.UI;

/// <summary>⭐⭐⭐ U-obs-5 (group 6) — the whole of what <see cref="EntityInspectorPanel"/> shows, this
/// frame. ⚠ A plain panel: no <see cref="PanelId"/>/<see cref="PanelKind"/> of its own — the HOST
/// (<c>IgEntityPropertiesWindow</c>) supplies both. 📄 <c>QUEUE_Panel_Observability_Sweep.md</c>
/// group 6.</summary>
public sealed record EntityInspectorPanelViewModel(
    string PanelId, string PanelKind, bool HasSelection, int EntityId, long TkbType,
    float PositionX, float PositionY, float PositionZ,
    string Affiliation, float DamageLevel) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ImGui panel displaying detailed ECS component data for the currently-selected
/// entity (IG.5.2).
///
/// Data is sourced from <see cref="EntityInspectorState"/>, which must be refreshed
/// each frame by the application shell before <see cref="Draw"/> is called.
///
/// Renders an "Entity Inspector" window showing:
/// <list type="bullet">
///   <item>Entity ID and TKB type.</item>
///   <item>World-space position (X, Y, Z) from <c>SimTransform</c>.</item>
///   <item>Force affiliation and damage level from <c>ResolvedStyle</c>.</item>
/// </list>
///
/// When no entity is selected the panel shows "No entity selected."
///
/// Visual states are driven through <see cref="EntityInspectorState"/>;
/// the panel itself is not unit-tested.
///
/// Call <see cref="Draw"/> each frame between <c>rlImGui.Begin()</c> and
/// <c>rlImGui.End()</c>.
/// </summary>
public class EntityInspectorPanel
{
    private readonly EntityInspectorState _state;

    /// <param name="state">Logic state instance refreshed by the application shell each frame.</param>
    public EntityInspectorPanel(EntityInspectorState state)
        => _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>
    /// Emits the Entity Properties ImGui window.
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        IgPanelColors.Push();
        bool panelVisible = ImGui.Begin("Entity Properties");
        IgPanelColors.Pop();
        if (!panelVisible) { ImGui.End(); return; }
        DrawContent();
        ImGui.End();
    }

    /// <summary>⭐⭐⭐ BUILD — a pure projection of <see cref="EntityInspectorState"/>. No ImGui, no
    /// Raylib — safe to call headless. 📄 §Example's BUILD step.</summary>
    public EntityInspectorPanelViewModel BuildViewModel(string panelId, string panelKind) => new(
        panelId, panelKind, _state.HasSelection, _state.EntityId, _state.TkbType,
        _state.PositionX, _state.PositionY, _state.PositionZ,
        _state.Affiliation.ToString(), _state.DamageLevel);

    /// <summary>
    /// Renders the panel content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent()
    {
        if (!_state.HasSelection)
        {
            ImGui.Text("No entity selected");
            return;
        }

        // ── Identity ──────────────────────────────────────────────────────────
        ImGui.Text($"Entity ID : {_state.EntityId}");
        ImGui.Text($"TKB Type  : {_state.TkbType}");
        ImGui.Separator();

        // ── Position ──────────────────────────────────────────────────────────
        ImGui.Text("Position (world m):");
        ImGui.Text($"  X : {_state.PositionX,10:F2}");
        ImGui.Text($"  Y : {_state.PositionY,10:F2}");
        ImGui.Text($"  Z : {_state.PositionZ,10:F2}");
        ImGui.Separator();

        // ── Resolved style ────────────────────────────────────────────────────
        ImGui.Text($"Affiliation : {_state.Affiliation}");
        ImGui.Text($"Damage      : {_state.DamageLevel:F1} %%");
    }
}
