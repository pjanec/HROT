using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S31: Bookmarks — set/jump bookmarks across a wide canvas with Ctrl+Shift+1..9 and Ctrl+1..9.</summary>
public sealed class S31_Bookmarks : Scenario
{
    public override string Name        => "31 — Bookmarks";
    public override string Description => "Pan to a region, press Ctrl+Shift+1 to set bookmark 1. Pan away. Press Ctrl+1 to jump back. Try cross-graph jump.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // ~50 nodes spread across a wide 5000×3000 region
        var rng   = new System.Random(7);
        var kinds = new[] { "Event.BeginPlay", "Flow.Branch", "Math.Multiply", "Math.Add", "Util.Print" };

        for (int i = 0; i < 50; i++)
        {
            var pos = new Vector2(rng.Next(0, 5000), rng.Next(0, 3000));
            AddNode(graph, catalog, kinds[i % kinds.Length], pos);
        }
    }
}
