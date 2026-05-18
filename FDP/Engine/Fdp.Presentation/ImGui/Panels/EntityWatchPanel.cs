using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Scenario;
using ImGuiNET;

using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

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
