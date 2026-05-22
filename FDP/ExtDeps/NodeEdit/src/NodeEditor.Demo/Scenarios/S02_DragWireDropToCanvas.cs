using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S02: Drag a wire and drop to empty canvas to trigger picker.</summary>
public sealed class S02_DragWireDropToCanvas : Scenario
{
    public override string Name        => "02 — Wire Drop → Picker";
    public override string Description => "Drag a wire from a pin and drop on empty canvas. A node picker should open.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Single event node — drag from its exec output to empty canvas
        AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
    }
}
