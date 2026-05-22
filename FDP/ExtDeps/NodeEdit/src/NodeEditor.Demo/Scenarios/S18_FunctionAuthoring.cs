using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S18: Function Authoring — navigate into a function body graph and wire nodes.</summary>
public sealed class S18_FunctionAuthoring : Scenario
{
    public override string Name        => "18 — Function Authoring";
    public override string Description => "Double-click 'ComputeDamage' in My Blueprint to navigate into the function body. Wire nodes, then Ctrl+S.";

    public override void Setup(FakeMyBlueprintModel mbModel)
    {
        mbModel.AddFunction("fn.compute_damage", "ComputeDamage");
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // EventGraph with a call site showing the function can be invoked
        var begin = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
        var print = AddNode(graph, catalog, "Util.Print",       new Vector2(400, 200));
        LinkNodes(graph, begin, 0, print, 0);
    }
}
