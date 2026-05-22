using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S33: Big Graph — 500 nodes, ~800 wires, 30 comments. Validates spatial-index + virtualization performance.</summary>
public sealed class S33_BigGraph : Scenario
{
    public override string Name        => "33 — Big Graph (500 nodes)";
    public override string Description => "Pan and zoom freely. FPS (status bar) should stay ≥ 60. Low-zoom culling kicks in below 0.5×.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var rng   = new System.Random(42);
        var kinds = new[] { "Math.Multiply", "Math.Add", "Flow.Branch", "Util.Print" };
        var nodes = new List<NodeId>(500);

        for (int i = 0; i < 500; i++)
        {
            var pos = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));
            nodes.Add(AddNode(graph, catalog, kinds[rng.Next(kinds.Length)], pos));
        }

        System.Diagnostics.Debug.Assert(graph.Nodes.Count == 500, "S33: Expected exactly 500 nodes.");

        // ~800 wires
        for (int i = 0; i < 800; i++)
        {
            var a = nodes[rng.Next(nodes.Count)];
            var b = nodes[rng.Next(nodes.Count)];
            if (a == b) continue;
            TryAddCompatibleLink(graph, a, b);
        }

        // 30 spread comments
        for (int i = 0; i < 30; i++)
        {
            var pos = new Vector2(rng.Next(0, 9500), rng.Next(0, 9500));
            graph.AddComment(IdGenerator.NewCommentId(), $"Region {i}", pos, new Vector2(500, 400),
                             new Vector4(0.3f, 0.3f, 0.5f, 0.4f), false);
        }
    }

    /// <summary>Try to link a random compatible output pin from <paramref name="fromId"/> to a random input pin of <paramref name="toId"/>.</summary>
    private static void TryAddCompatibleLink(FakeGraphModel graph, NodeId fromId, NodeId toId)
    {
        try
        {
            var fromNode = (FakeNodeModel?)graph.FindNode(fromId);
            var toNode   = (FakeNodeModel?)graph.FindNode(toId);
            if (fromNode is null || toNode is null) return;

            var outputs = fromNode.Pins.Where(p => p.Direction == PinDirection.Output).ToList();
            var inputs  = toNode.Pins.Where(p => p.Direction == PinDirection.Input).ToList();
            if (outputs.Count == 0 || inputs.Count == 0) return;

            var fromPin = outputs[0];
            var toPin   = inputs[0];

            // Skip if link would create a duplicate (same pins already connected)
            if (graph.Links.Any(l => l.FromPin == fromPin.Id && l.ToPin == toPin.Id))
                return;

            graph.AddLink(IdGenerator.NewLinkId(), fromPin.Id, toPin.Id);
        }
        catch (InvalidOperationException)
        {
            // Skip silently on validation failure
        }
    }
}
