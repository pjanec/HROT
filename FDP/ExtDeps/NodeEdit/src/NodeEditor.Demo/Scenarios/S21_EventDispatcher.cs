using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S21: Event Dispatcher — place Call, Bind, and Unbind nodes for a dispatcher.</summary>
public sealed class S21_EventDispatcher : Scenario
{
    public override string Name        => "21 — Event Dispatcher";
    public override string Description => "Drag 'OnHealthChanged' from My Blueprint. Pick Call, Bind, or Unbind from the popup to place node variants.";

    public override void Setup(FakeMyBlueprintModel mbModel)
    {
        mbModel.AddDispatcher("disp.health_changed", "OnHealthChanged");
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var begin = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 200));
        var print = AddNode(graph, catalog, "Util.Print",        new Vector2(380, 200));
        LinkNodes(graph, begin, 0, print, 0);
    }
}
