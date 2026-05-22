using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S01: Three nodes, two wires. Exercises pan/zoom/select/drag.</summary>
public sealed class S01_HelloCanvas : Scenario
{
    public override string Name        => "01 — Hello Canvas";
    public override string Description => "Three nodes, two wires. Try pan (RMB), zoom (wheel), select, drag.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var beginPlay = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
        var print     = AddNode(graph, catalog, "Util.Print",      new Vector2(350, 200));
        var delay     = AddNode(graph, catalog, "Flow.Delay",      new Vector2(600, 200));

        LinkNodes(graph, beginPlay, 0, print, 0);  // exec chain
        LinkNodes(graph, print,     0, delay, 0);
    }
}
