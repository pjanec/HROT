using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S30: Go to Definition — F12 from a function call navigates to the function's body graph.</summary>
public sealed class S30_GoToDefinition : Scenario
{
    public override string Name        => "30 — Go to Definition";
    public override string Description => "Select the 'ComputeDamage' call node, press F12 → navigates to function body. Select 'Get Health', F12 → My Blueprint scrolls to Health.";

    public override void Setup(FakeMyBlueprintModel mbModel)
    {
        mbModel.AddFunction("fn.compute_damage2", "ComputeDamage");
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var begin = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 200));
        var getHp = AddNode(graph, catalog, "Util.GetVar",      new Vector2(280, 380));
        var mul   = AddNode(graph, catalog, "Math.Multiply",    new Vector2(460, 200));
        var print = AddNode(graph, catalog, "Util.Print",       new Vector2(660, 200));

        // ComputeDamage call node (represented as Multiply with renamed title)
        if (graph.FindNode(mul) is FakeNodeModel mn) mn.Title = "ComputeDamage";
        // GetHealth variable node
        if (graph.FindNode(getHp) is FakeNodeModel gn) gn.Title = "Get Health";

        LinkNodes(graph, begin, 0, print, 0);
        LinkNodes(graph, mul,   0, print, 1);
    }
}
