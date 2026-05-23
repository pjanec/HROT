using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S34: Nodes with attachment pills of various categories and states.</summary>
public sealed class S34_NodeAttachments : Scenario
{
    public override string Name        => "34 -- Node Attachments";
    public override string Description => "Nodes with decorator/flag/pure/custom pills. Zoom out below 0.5 to see low-zoom bars.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Node 1: two Decorator attachments.
        var n1 = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
        graph.AddAttachment(n1, AttachmentCategory.Decorator, "I", "Inverter", stackIndex: 0);
        graph.AddAttachment(n1, AttachmentCategory.Decorator, "R", "Repeat x3", stackIndex: 1);

        // Node 2: Flag + Pure with Error state.
        var n2 = AddNode(graph, catalog, "Util.Print", new Vector2(400, 200));
        graph.AddAttachment(n2, AttachmentCategory.Flag, "H", "Has History", stackIndex: 0);
        var errAtch = graph.AddAttachment(n2, AttachmentCategory.Pure, "P", "Pure", stackIndex: 1);
        errAtch.State = AttachmentState.Error;

        // Node 3: Custom category with Warning state.
        var n3 = AddNode(graph, catalog, "Flow.Delay", new Vector2(700, 200));
        var warnAtch = graph.AddAttachment(n3, AttachmentCategory.Custom, null, "Custom Tag", stackIndex: 0);
        warnAtch.State = AttachmentState.Warning;

        // Node 4: Many attachments to exercise row wrapping.
        var n4 = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 450));
        for (int i = 0; i < 6; i++)
            graph.AddAttachment(n4, (AttachmentCategory)(i % 4), null, "Tag " + (i + 1), stackIndex: i);

        // Wires for visual context.
        LinkNodes(graph, n1, 0, n2, 0);
        LinkNodes(graph, n2, 0, n3, 0);
    }
}
