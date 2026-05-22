using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S16: Promote to Variable — RMB an unconnected input pin to promote it to a named variable.</summary>
public sealed class S16_PromoteToVariable : Scenario
{
    public override string Name        => "16 — Promote to Variable";
    public override string Description => "RMB the 'A' input pin on the Multiply node → 'Promote to Variable…'. Name it 'Multiplier'.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Math.Multiply", new Vector2(300, 200));
    }
}
