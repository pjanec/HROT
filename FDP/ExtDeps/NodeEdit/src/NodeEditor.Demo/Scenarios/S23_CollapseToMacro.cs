using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S23: Collapse to Macro — selection includes a latent Delay node, so Ctrl+E requires a Macro.</summary>
public sealed class S23_CollapseToMacro : Scenario
{
    public override string Name        => "23 — Collapse to Macro";
    public override string Description => "Select the Sequence + Delay + Print nodes, press Ctrl+E. The dialog will require Macro because of the latent Delay.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var begin = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 200));
        var seq   = AddNode(graph, catalog, "Flow.Sequence",    new Vector2(320, 200));
        var delay = AddNode(graph, catalog, "Flow.Delay",       new Vector2(540, 200));
        var print = AddNode(graph, catalog, "Util.Print",       new Vector2(760, 200));

        LinkNodes(graph, begin, 0, seq,   0);
        LinkNodes(graph, seq,   0, delay, 0);
        LinkNodes(graph, delay, 0, print, 0);
    }
}
