using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S06: Wire with reroute waypoints; drag and delete reroutes.</summary>
public sealed class S06_Reroutes : Scenario
{
    public override string Name        => "06 — Reroutes";
    public override string Description => "Wire with reroute points. Drag a reroute dot or double-click wire to insert one.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var n1 = AddNode(graph, catalog, "Math.Add",      new Vector2(100, 200));
        var n2 = AddNode(graph, catalog, "Math.Multiply", new Vector2(550, 300));

        // Link with a reroute waypoint
        var n1Node   = (FakeNodeModel)graph.FindNode(n1)!;
        var n2Node   = (FakeNodeModel)graph.FindNode(n2)!;
        var fromPin  = n1Node.Pins.First(p => p.Direction == PinDirection.Output);
        var toPin    = n2Node.Pins.First(p => p.Direction == PinDirection.Input);

        var linkId = IdGenerator.NewLinkId();
        var link   = graph.AddLink(linkId, fromPin.Id, toPin.Id);
        link.AddWaypoint(new Vector2(350, 380)); // reroute in middle
    }
}
