using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S10: Type picker — nested category tree layout.</summary>
public sealed class S10_TypePicker : Scenario
{
    public override string Name        => "10 — Type Picker (Nested)";
    public override string Description => "Click 'Pick Type' in the overlay to open a Tree-layout type picker.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Math.Add",      new Vector2(200, 200));
        AddNode(graph, catalog, "Math.Multiply", new Vector2(450, 200));
    }
}
