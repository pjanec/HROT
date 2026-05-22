using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S28: Find in Graph — large graph with errors; exercise Ctrl+F, prefixes, and F3 cycling.</summary>
public sealed class S28_FindInGraph : Scenario
{
    public override string Name        => "28 — Find in Graph";
    public override string Description => "Press Ctrl+F. Type 'multiply' → 5 hits. Try 'error:' or 'kind:branch'. F3 cycles; Esc closes.";

    private FakeDebugSession? _session;
    public override FakeDebugSession? Session => _session;

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var allNodes = new List<NodeId>();

        // Row 1: BeginPlay → Branch → Print (×4)
        var begin  = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 100));
        var branch1 = AddNode(graph, catalog, "Flow.Branch",    new Vector2(300, 100));
        allNodes.Add(begin); allNodes.Add(branch1);
        LinkNodes(graph, begin, 0, branch1, 0);

        for (int i = 0; i < 4; i++)
        {
            var p = AddNode(graph, catalog, "Util.Print", new Vector2(500, 60 + i * 80));
            allNodes.Add(p);
        }

        // 5 Multiply nodes
        for (int i = 0; i < 5; i++)
        {
            var m = AddNode(graph, catalog, "Math.Multiply", new Vector2(750 + i * 180, 100 + i * 60));
            allNodes.Add(m);
        }

        // Additional Branch nodes (for kind:branch test)
        for (int i = 0; i < 4; i++)
        {
            var b = AddNode(graph, catalog, "Flow.Branch", new Vector2(300, 300 + i * 100));
            allNodes.Add(b);
        }

        // Math variety
        for (int i = 0; i < 6; i++)
        {
            var kind = i % 2 == 0 ? "Math.Add" : "Math.Subtract";
            allNodes.Add(AddNode(graph, catalog, kind, new Vector2(150 + i * 160, 600)));
        }

        // 3 nodes with Error state
        for (int i = 0; i < 3; i++)
        {
            var errNode = AddNode(graph, catalog, "Util.Print", new Vector2(200 + i * 200, 800));
            if (graph.FindNode(errNode) is FakeNodeModel fn)
                fn.State = NodeState.Error;
            allNodes.Add(errNode);
        }

        // Debug session with breakpoints on 2 nodes (for state filtering)
        _session = new FakeDebugSession(allNodes.ToArray());
        if (allNodes.Count >= 2)
        {
            _session.ToggleBreakpoint(allNodes[0]);
            _session.ToggleBreakpoint(allNodes[1]);
        }
    }
}
