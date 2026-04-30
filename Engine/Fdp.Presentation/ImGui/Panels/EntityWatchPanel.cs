using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
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
    private static readonly Vector4 ExConViolet = new Vector4(0.32f, 0.08f, 0.48f, 1f);

    /// <summary>
    /// The <see cref="ComponentReflector"/> used to draw component details.
    /// Expose to allow host subsystems to wire up the component editor
    /// (e.g. <c>panel.Reflector.EditWindowManager = ...</c>).
    /// </summary>
    public ComponentReflector Reflector => _reflector;

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

        bool isSingleton = _targetEntity == RepositoryAdapter.SingletonEntity;

        if (isSingleton)
        {
            ImGuiApi.TextUnformatted("[Singletons]");
        }
        else
        {
            long? netId = null;
            if (session.HasComponent(_targetEntity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity)))
            {
                var comp = session.GetComponent(_targetEntity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity));
                if (comp is Fdp.Toolkit.Replication.Components.NetworkIdentity ni)
                    netId = ni.Value;
            }

            ImGuiApi.TextUnformatted($"[{_targetEntity.Index}, v{_targetEntity.Generation}]");
            if (netId.HasValue)
            {
                ImGuiApi.SameLine();
                ImGuiApi.TextColored(ExConViolet, $"({netId.Value})");
            }
        }

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
