using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S19: Multiple Return Nodes — function body with a Branch routing two Return nodes.</summary>
public sealed class S19_MultipleReturnNodes : Scenario
{
    public override string Name        => "19 — Multiple Return Nodes";
    public override string Description => "RMB the canvas → 'Add Return Node' to place a second Return. Wire Branch True/False exec outputs to each Return.";

    public override void Setup(FakeMyBlueprintModel mbModel)
    {
        mbModel.AddFunction("fn.is_alive", "IsAlive");
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Simulate the body of IsAlive(health: float) → bool
        // Entry → Branch (health > 0) — user must wire the Returns
        var entry   = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2( 80, 220));
        var branch  = AddNode(graph, catalog, "Flow.Branch",       new Vector2(320, 220));
        var print1  = AddNode(graph, catalog, "Util.Print",        new Vector2(580, 120));
        var print2  = AddNode(graph, catalog, "Util.Print",        new Vector2(580, 340));

        LinkNodes(graph, entry,  0, branch, 0);
        LinkNodes(graph, branch, 0, print1, 0);
        LinkNodes(graph, branch, 1, print2, 0);
    }
}
