using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Scenario;
using ImGuiNET;

using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="EntityWatchPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⚠ Not the same concept as
/// <c>PanelIds.Watch</c> (the blueprint pinned-variable watch) — different source, different columns
/// — so <c>PanelKind</c> stays a local literal on the host rather than reusing that constant.
/// </summary>
public sealed record EntityWatchPanelViewModel(
    string PanelId,
    string PanelKind,
    bool EntityAlive,
    int TargetEntityIndex,
    int TargetEntityGeneration,
    IReadOnlyList<string> ComponentTypeNames) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Reusable panel that renders the component tree for a single fixed entity.
/// Intended to be hosted inside a volatile <c>FdpEntityWatchWindow</c>.
/// Each instance owns its own <see cref="ComponentReflector"/> so diff-caching
/// (yellow text for changed fields) works independently per watch window.
/// </summary>
public class EntityWatchPanel
{
    private readonly Entity _targetEntity;
    private readonly ComponentReflector _reflector = new();

    /// <summary>
    /// The <see cref="ComponentReflector"/> used to draw component details.
    /// Expose to allow host subsystems to wire up the component editor
    /// (e.g. <c>panel.Reflector.EditWindowManager = ...</c>).
    /// </summary>
    public ComponentReflector Reflector => _reflector;

    /// <summary>
    /// When set, single-component copy uses the unified scenario serialization path
    /// with custom IEntityScenarioTranslator logic.
    /// </summary>
    public ScenarioSerializer? Serializer { get; set; }

    public EntityWatchPanel(Entity targetEntity)
    {
        _targetEntity = targetEntity;
        _reflector.CopyComponentJsonFunc = (s, e, t, d) => InspectorJsonUtils.BuildComponentJson(s, e, t, d, Serializer);
    }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure projection of the target entity's attached component types. No ImGui.</summary>
    public EntityWatchPanelViewModel BuildViewModel(IInspectableSession session, string panelId, string panelKind)
    {
        bool alive = session.IsAlive(_targetEntity);
        var names = new List<string>();
        if (alive)
        {
            foreach (var t in session.GetAllComponentTypes())
                if (session.HasComponent(_targetEntity, t))
                    names.Add(t.Name);
            names.Sort(System.StringComparer.Ordinal);
        }

        return new EntityWatchPanelViewModel(
            panelId, panelKind, alive, _targetEntity.Index, _targetEntity.Generation, names);
    }

    /// <summary>
    /// Renders the watch panel content inside the current ImGui window.
    /// </summary>
    public void DrawContent(IInspectableSession session)
    {
        if (!session.IsAlive(_targetEntity))
        {
            ImGuiApi.TextDisabled("Entity no longer exists.");
            return;
        }

        EntityHeaderDrawer.DrawEntityHeader(session, _targetEntity, () =>
        {
            var json = EntityJsonDumper.Dump(session, _targetEntity);
            ImGuiApi.SetClipboardText(json);
        });

        if (ImGuiApi.SmallButton(">> Expand All")) _reflector.ForceExpandAll = true;
        ImGuiApi.SameLine();
        if (ImGuiApi.SmallButton("<< Collapse All")) _reflector.ForceCollapseAll = true;

        ImGuiApi.Separator();

        _reflector.DrawComponents(session, _targetEntity);
    }
}
