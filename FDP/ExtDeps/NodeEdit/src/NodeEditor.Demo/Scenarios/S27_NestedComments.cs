using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S27: Nested Comments — three overlapping/nested comments demonstrating z-order behaviour.</summary>
public sealed class S27_NestedComments : Scenario
{
    public override string Name        => "27 — Nested Comments";
    public override string Description => "Comment B is inside A; C overlaps both. Click headers to select each. RMB → Send to Back / Bring to Front.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Nodes scattered across three regions
        AddNode(graph, catalog, "Event.BeginPlay",  new Vector2(160, 180));
        AddNode(graph, catalog, "Flow.Branch",       new Vector2(380, 180));
        AddNode(graph, catalog, "Util.Print",        new Vector2(580, 120));
        AddNode(graph, catalog, "Math.Multiply",     new Vector2(380, 360));
        AddNode(graph, catalog, "Math.Add",          new Vector2(580, 360));
        AddNode(graph, catalog, "Flow.Sequence",     new Vector2(700, 200));

        // Comment A — outermost
        graph.AddComment(
            IdGenerator.NewCommentId(),
            "Comment A (outer)",
            new Vector2( 60,  80),
            new Vector2(720, 360),
            new Vector4(0.20f, 0.50f, 0.80f, 0.40f),
            true);

        // Comment B — fully inside A
        graph.AddComment(
            IdGenerator.NewCommentId(),
            "Comment B (inner)",
            new Vector2(220, 130),
            new Vector2(340, 180),
            new Vector4(0.80f, 0.60f, 0.10f, 0.50f),
            true);

        // Comment C — overlaps A and B
        graph.AddComment(
            IdGenerator.NewCommentId(),
            "Comment C (overlap)",
            new Vector2(450, 100),
            new Vector2(380, 300),
            new Vector4(0.10f, 0.70f, 0.30f, 0.45f),
            true);
    }
}
