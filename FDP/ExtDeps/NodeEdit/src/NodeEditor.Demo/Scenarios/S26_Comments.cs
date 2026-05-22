using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S26: Comments — create comments, drag with contents, rename, recolor, and resize.</summary>
public sealed class S26_Comments : Scenario
{
    public override string Name        => "26 — Comments";
    public override string Description => "Select nodes, press C to comment them. Drag the header. Hold Shift to move comment alone. RMB to recolor. Double-click to rename.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // 6 loosely arranged nodes
        var n1 = AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(100, 150));
        var n2 = AddNode(graph, catalog, "Flow.Branch",       new Vector2(340, 150));
        var n3 = AddNode(graph, catalog, "Util.Print",        new Vector2(600, 80));
        var n4 = AddNode(graph, catalog, "Util.Print",        new Vector2(600, 280));
        var n5 = AddNode(graph, catalog, "Math.Multiply",     new Vector2(340, 400));
        var n6 = AddNode(graph, catalog, "Math.Add",          new Vector2(560, 400));

        LinkNodes(graph, n1, 0, n2, 0);
        LinkNodes(graph, n2, 0, n3, 0);
        LinkNodes(graph, n2, 1, n4, 0);
        LinkNodes(graph, n5, 0, n6, 0);

        // Pre-placed example comment
        graph.AddComment(
            IdGenerator.NewCommentId(),
            "Boss Phase 2 Logic",
            new Vector2(60, 110),
            new Vector2(600, 240),
            new Vector4(0.10f, 0.35f, 0.75f, 0.60f),
            true);
    }
}
