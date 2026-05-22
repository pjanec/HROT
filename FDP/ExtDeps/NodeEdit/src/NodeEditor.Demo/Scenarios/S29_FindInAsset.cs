using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S29: Find in Asset — multi-graph asset search with Ctrl+Shift+F.</summary>
public sealed class S29_FindInAsset : Scenario
{
    public override string Name        => "29 — Find in Asset";
    public override string Description => "Press Ctrl+Shift+F to open asset-scope search. Type 'multiply' — results are grouped by graph. Click a result to navigate.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Single-graph setup with Multiply nodes for the find demo
        var begin = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
        for (int i = 0; i < 4; i++)
            AddNode(graph, catalog, "Math.Multiply", new Vector2(320 + i * 200, 200));
        for (int i = 0; i < 3; i++)
            AddNode(graph, catalog, "Math.Add", new Vector2(320 + i * 200, 380));

        var branch = AddNode(graph, catalog, "Flow.Branch", new Vector2(300, 560));
        LinkNodes(graph, begin, 0, branch, 0);
    }
}
