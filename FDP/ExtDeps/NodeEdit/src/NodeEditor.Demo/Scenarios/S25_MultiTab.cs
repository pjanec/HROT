using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S25: Multi-Tab — three graphs open simultaneously; use Ctrl+Tab to cycle between them.</summary>
public sealed class S25_MultiTab : Scenario
{
    public override string Name        => "25 — Multi-Tab";
    public override string Description => "Three graphs are open. Click tabs or press Ctrl+Tab / Ctrl+Shift+Tab to cycle. Each tab has its own viewport.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Not used — BuildMultiGraph drives this scenario
    }

    public override FakeGraphContainer? BuildMultiGraph(FakeNodeCatalog catalog, out FakeMyBlueprintModel myBlueprint)
    {
        myBlueprint = null!;

        var eventGraph   = new FakeGraphModel(GraphId.NewId(), "EventGraph");
        var computeGraph = new FakeGraphModel(GraphId.NewId(), "ComputeDamage");
        var enemyGraph   = new FakeGraphModel(GraphId.NewId(), "OnEnemyKilled");

        // Populate EventGraph
        var begin  = AddNodeToGraph(eventGraph, catalog, "Event.BeginPlay", new Vector2(100, 200));
        var branch = AddNodeToGraph(eventGraph, catalog, "Flow.Branch",      new Vector2(360, 200));
        var print  = AddNodeToGraph(eventGraph, catalog, "Util.Print",       new Vector2(600, 120));
        LinkGraphNodes(eventGraph, begin,  0, branch, 0);
        LinkGraphNodes(eventGraph, branch, 0, print,  0);

        // Populate ComputeDamage function body
        var entry2 = AddNodeToGraph(computeGraph, catalog, "Event.BeginPlay", new Vector2(80,  200));
        var mul    = AddNodeToGraph(computeGraph, catalog, "Math.Multiply",   new Vector2(340, 200));
        var clamp  = AddNodeToGraph(computeGraph, catalog, "Math.Clamp",      new Vector2(580, 200));
        LinkGraphNodes(computeGraph, entry2, 0, clamp, 0);
        LinkGraphNodes(computeGraph, mul,    0, clamp, 0);

        // Populate OnEnemyKilled event body
        var entry3 = AddNodeToGraph(enemyGraph, catalog, "Event.BeginPlay", new Vector2(80,  200));
        var seq    = AddNodeToGraph(enemyGraph, catalog, "Flow.Sequence",   new Vector2(340, 200));
        var p2     = AddNodeToGraph(enemyGraph, catalog, "Util.Print",      new Vector2(600, 120));
        var p3     = AddNodeToGraph(enemyGraph, catalog, "Util.Print",      new Vector2(600, 300));
        LinkGraphNodes(enemyGraph, entry3, 0, seq, 0);
        LinkGraphNodes(enemyGraph, seq,    0, p2,  0);
        LinkGraphNodes(enemyGraph, seq,    1, p3,  0);

        return new FakeGraphContainer(eventGraph, computeGraph, enemyGraph);
    }

    // ── graph-level helpers (can't use inherited helpers as they target a specific graph) ─

    private static NodeId AddNodeToGraph(FakeGraphModel graph, FakeNodeCatalog catalog, string kindId, Vector2 pos)
    {
        var id    = IdGenerator.NewNodeId();
        var entry = catalog.All.First(e => e.Kind.Id == kindId);
        var node  = graph.AddNode(id, entry.Kind, entry.DisplayName, pos);
        foreach (var sig in entry.Inputs)  node.AddPin(sig.Label, PinDirection.Input,  sig.Kind, sig.Type);
        foreach (var sig in entry.Outputs) node.AddPin(sig.Label, PinDirection.Output, sig.Kind, sig.Type);
        return id;
    }

    private static void LinkGraphNodes(FakeGraphModel graph, NodeId fromNode, int fromPinIndex, NodeId toNode, int toPinIndex)
    {
        var fromN  = (FakeNodeModel)graph.FindNode(fromNode)!;
        var toN    = (FakeNodeModel)graph.FindNode(toNode)!;
        var fromPin = fromN.Pins.Where(p => p.Direction == PinDirection.Output).ElementAt(fromPinIndex);
        var toPin   = toN.Pins.Where(p => p.Direction == PinDirection.Input).ElementAt(toPinIndex);
        graph.AddLink(IdGenerator.NewLinkId(), fromPin.Id, toPin.Id);
    }
}
