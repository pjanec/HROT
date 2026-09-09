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
    internal string[] GetSensorVariableNamesForTest()
        => _parent.Declarations.Of(DeclarationKind.Variable)
            .Where(d => d.Type.TypeId == "FDP.Eqs.EqsSensorHandle")
            .Select(d => d.Name)
            .ToArray();

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

        var sensorVars = GetSensorVariableNamesForTest();

        int sensorIdx = Array.IndexOf(sensorVars, _node.SensorVariableName);
        if (ImGui.Combo("Sensor", ref sensorIdx, sensorVars, sensorVars.Length))
        {
            _node.SensorVariableName = sensorVars[sensorIdx];
            IsDirty = true;
        }

        if (sensorVars.Length == 0)
            ImGui.TextColored(EditorColors.Info, "(no EqsSensorHandle variables on this asset)");

        ImGui.TextDisabled("Index: drive via input pin (default 0)");
        ImGui.TextDisabled("Outputs: IsReady, ResultCount, Entity, Position, Score");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
