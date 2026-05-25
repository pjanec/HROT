using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

public sealed class SpawnEqsSensorNodeDrawer : IBlueprintNodeDrawer
{
    private readonly EqsTemplateRegistry _eqsTemplates;

    public SpawnEqsSensorNodeDrawer(EqsTemplateRegistry eqsTemplates)
    {
        _eqsTemplates = eqsTemplates ?? throw new ArgumentNullException(nameof(eqsTemplates));
    }

    public bool Handles(Node node) => node is SpawnEqsSensorNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new SpawnEqsSensorNodeSession(
            (SpawnEqsSensorNode)node, parentAsset, _eqsTemplates);
}

internal sealed class SpawnEqsSensorNodeSession : INodeEditSession
{
    private readonly SpawnEqsSensorNode _node;
    private readonly BlueprintAsset _parent;
    private readonly EqsTemplateRegistry _templates;

    public bool IsDirty { get; private set; }

    public SpawnEqsSensorNodeSession(
        SpawnEqsSensorNode node,
        BlueprintAsset parentAsset,
        EqsTemplateRegistry templates)
    {
        _node      = node;
        _parent    = parentAsset;
        _templates = templates;
    }

    /// <summary>
    /// Test hook: simulates the designer selecting a template by AssetId.
    /// Sets TemplateAssetId on the node and marks session dirty.
    /// (InternalsVisibleTo Hrot.Blueprints.Tests)
    /// </summary>
    internal void SelectTemplateForTest(Guid assetId)
    {
        _node.TemplateAssetId = assetId;
        IsDirty = true;
    }

    public void Draw()
    {
        ImGui.Text("Spawn EQS Sensor");
        ImGui.Separator();
        DrawDispatchGuard();
        DrawTemplatePicker();
        ImGui.Separator();
        ImGui.TextDisabled("Inputs (wire via pins, or use literal defaults):");
        ImGui.TextDisabled("  • SearchRadius     (float)");
        ImGui.TextDisabled("  • FactionFilter    (uint)");
        ImGui.TextDisabled("  • ThreatThreshold  (float)");
        ImGui.TextDisabled("  • PublishPolicy    (byte)");
        ImGui.TextDisabled("  • Priority         (byte)");
        ImGui.TextDisabled("Output: Handle (EqsSensorHandle)");
    }

    private void DrawDispatchGuard()
    {
        if (_parent.Dispatch != BlueprintDispatchKind.Instance)
        {
            ImGui.TextColored(EditorColors.Error,
                "⚠ SpawnEqsSensorNode is only allowed in Instance Blueprints.");
            ImGui.Separator();
        }
    }

    private void DrawTemplatePicker()
    {
        var templates    = _templates.EnumerateAll();
        var displayNames = templates.Select(t => t.DisplayName).ToArray();

        int currentIdx = -1;
        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i].AssetId == _node.TemplateAssetId) { currentIdx = i; break; }
        }

        if (ImGui.Combo("Template", ref currentIdx, displayNames, displayNames.Length))
        {
            if (currentIdx >= 0)
            {
                var chosen = templates[currentIdx];
                if (chosen.AssetId != _node.TemplateAssetId)
                {
                    _node.TemplateAssetId = chosen.AssetId;
                    IsDirty = true;
                }
            }
        }

        if (_node.TemplateAssetId == Guid.Empty)
            ImGui.TextColored(EditorColors.Warning, "(no template selected)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
