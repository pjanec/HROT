using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S12: Grid-layout asset picker with fake assets.</summary>
public sealed class S12_AssetGridPicker : Scenario
{
    public override string Name        => "12 — Asset Grid Picker";
    public override string Description => "Click 'Pick Asset' to open a Grid-layout picker showing fake asset entries.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
    }
}
