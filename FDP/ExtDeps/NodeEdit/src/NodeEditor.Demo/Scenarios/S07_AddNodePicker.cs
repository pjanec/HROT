using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S07: Tab on canvas → opens node picker (Standard layout).</summary>
public sealed class S07_AddNodePicker : Scenario
{
    public override string Name        => "07 — Add Node Picker";
    public override string Description => "Press Tab on the canvas to open the Add Node picker (Standard layout).";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Single event node to give context
        AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
    }
}
