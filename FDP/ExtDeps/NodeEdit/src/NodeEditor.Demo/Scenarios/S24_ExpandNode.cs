using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S24: Expand Node — RMB a function call node to inline (expand) its body in place.</summary>
public sealed class S24_ExpandNode : Scenario
{
    public override string Name        => "24 — Expand Node";
    public override string Description => "RMB the 'ScaleBy' function call node → 'Expand Node'. The call is replaced by inlined Multiply nodes.";

    public override void Setup(FakeMyBlueprintModel mbModel)
    {
        mbModel.AddFunction("fn.scale_by", "ScaleBy");
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var begin   = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 200));
        // Function call represented as a simple Multiply node in the demo
        var scaleBy = AddNode(graph, catalog, "Math.Multiply",    new Vector2(360, 200));
        var print   = AddNode(graph, catalog, "Util.Print",       new Vector2(600, 200));

        // Change title to look like a function call
        if (graph.FindNode(scaleBy) is FakeNodeModel scaleNode)
            scaleNode.Title = "ScaleBy";

        LinkNodes(graph, begin,   0, print, 0);
        LinkNodes(graph, scaleBy, 0, print, 1);
    }
}
