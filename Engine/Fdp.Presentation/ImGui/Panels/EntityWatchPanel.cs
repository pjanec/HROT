using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
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

    public EntityWatchPanel(Entity targetEntity)
    {
        _targetEntity = targetEntity;
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

        ImGuiApi.Text($"ID: {_targetEntity.Index} | Gen: {_targetEntity.Generation}");

        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Copy JSON"))
        {
            var json = EntityJsonDumper.Dump(session, _targetEntity);
            ImGuiApi.SetClipboardText(json);
        }
        if (ImGuiApi.IsItemHovered())
            ImGuiApi.SetTooltip("Dump exact entity state to clipboard as JSON");

        if (session.IsReadOnly)
            ImGuiApi.TextColored(new Vector4(1, 1, 0, 1), "[READ-ONLY]");

        if (ImGuiApi.SmallButton(">> Expand All")) _reflector.ForceExpandAll = true;
        ImGuiApi.SameLine();
        if (ImGuiApi.SmallButton("<< Collapse All")) _reflector.ForceCollapseAll = true;

        ImGuiApi.Separator();

        _reflector.DrawComponents(session, _targetEntity);
    }
}
