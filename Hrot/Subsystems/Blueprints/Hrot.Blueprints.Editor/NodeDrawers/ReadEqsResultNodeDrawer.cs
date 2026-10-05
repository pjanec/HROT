using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

public sealed class ReadEqsResultNodeDrawer : IBlueprintNodeDrawer
{
    public bool Handles(Node node) => node is ReadEqsResultNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new ReadEqsResultNodeSession((ReadEqsResultNode)node, parentAsset);
}

internal sealed class ReadEqsResultNodeSession : INodeEditSession
{
    private readonly ReadEqsResultNode _node;
    private readonly BlueprintAsset _parent;

    public bool IsDirty { get; private set; }

    public ReadEqsResultNodeSession(ReadEqsResultNode node, BlueprintAsset parentAsset)
    {
        _node   = node;
        _parent = parentAsset;
    }

    /// <summary>
    /// Returns the names of all EqsSensorHandle-typed variables on the asset.
    /// Internal test hook (InternalsVisibleTo Hrot.Blueprints.Tests).
    /// </summary>
    internal string[] GetSensorVariableNamesForTest() => EqsSensorSourcePicker.SensorVariables(_parent);

    public void Draw()
    {
        ImGui.Text("Read EQS Result");
        ImGui.Separator();

        if (_parent.Dispatch != BlueprintDispatchKind.Instance)
        {
            ImGui.TextColored(EditorColors.Error,
                "⚠ ReadEqsResultNode is only allowed in Instance Blueprints.");
            ImGui.Separator();
        }

        // ⭐ CE-3054 D — a sensor variable, or the unit's own sensor of a kind (EqsSensorSourcePicker).
        string name = _node.SensorVariableName;
        byte kind = _node.UnitSensorKind;
        if (EqsSensorSourcePicker.Draw("Sensor", _parent, ref name, ref kind))
        {
            _node.SensorVariableName = name;
            _node.UnitSensorKind = kind;
            IsDirty = true;
        }

        ImGui.TextDisabled("Index: drive via input pin (default 0)");
        ImGui.TextDisabled("Outputs: IsReady, ResultCount, Entity, Position, Score");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
