using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S03: Marquee select multiple nodes then drag them as a group.</summary>
public sealed class S03_BoxSelectAndDrag : Scenario
{
    public override string Name        => "03 — Box Select + Multi-Drag";
    public override string Description => "Draw a marquee selection over nodes, then drag the selection.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 200));
        AddNode(graph, catalog, "Math.Add",          new Vector2(350, 150));
        AddNode(graph, catalog, "Math.Multiply",     new Vector2(350, 300));
        AddNode(graph, catalog, "Util.Print",        new Vector2(600, 200));
    }
}
