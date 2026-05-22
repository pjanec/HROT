using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S32: Hot-Reload Conflict — make the graph dirty, then click 'Simulate External Modify' to trigger the conflict toast.</summary>
public sealed class S32_HotReloadConflict : Scenario
{
    public override string Name        => "32 — Hot-Reload Conflict";
    public override string Description => "Press 'Make Dirty' (menu bar), then 'Simulate External Modify'. A blocking toast appears with Save / Discard / Ignore.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var begin = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 200));
        var print = AddNode(graph, catalog, "Util.Print",       new Vector2(380, 200));

        // Rename a node to simulate a dirty edit that's been made
        if (graph.FindNode(print) is FakeNodeModel fn)
            fn.Title = "Renamed Node (dirty edit)";

        LinkNodes(graph, begin, 0, print, 0);
    }
}
