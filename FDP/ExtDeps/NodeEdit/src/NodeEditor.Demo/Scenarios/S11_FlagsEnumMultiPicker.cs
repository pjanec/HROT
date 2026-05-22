using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S11: Flags enum multi-select picker.</summary>
public sealed class S11_FlagsEnumMultiPicker : Scenario
{
    public override string Name        => "11 — Flags Enum Multi-Picker";
    public override string Description => "Click 'Pick Flags' to open a multi-select Compact-layout picker.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Flow.Branch", new Vector2(200, 200));
    }
}
