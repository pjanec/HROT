using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S20: Macro with Wildcards — place a macro call and observe wildcard pin resolution.</summary>
public sealed class S20_MacroWithWildcards : Scenario
{
    public override string Name        => "20 — Macro with Wildcards";
    public override string Description => "Drag 'ForEachWithBreak' from My Blueprint. Wire a typed array into 'Array' to resolve the wildcard pins.";

    public override void Setup(FakeMyBlueprintModel mbModel)
    {
        mbModel.AddMacro("macro.for_each", "ForEachWithBreak");
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var begin = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 200));
        var loop  = AddNode(graph, catalog, "Flow.ForLoop",      new Vector2(350, 200));
        var print = AddNode(graph, catalog, "Util.Print",        new Vector2(650, 200));

        LinkNodes(graph, begin, 0, loop,  0);
        LinkNodes(graph, loop,  0, print, 0);
    }
}
