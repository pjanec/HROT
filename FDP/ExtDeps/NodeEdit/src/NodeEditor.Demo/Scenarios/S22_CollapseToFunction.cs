using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S22: Collapse to Function — select 5 damage-computation nodes, press Ctrl+E to refactor into a function call.</summary>
public sealed class S22_CollapseToFunction : Scenario
{
    public override string Name        => "22 — Collapse to Function (Ctrl+E)";
    public override string Description => "Marquee-select all 5 math nodes, press Ctrl+E, name the function 'CalculateDamage', confirm.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // 5-node damage subgraph: Base * Multiplier + Bonus - Resistance → Clamp
        var getBase = AddNode(graph, catalog, "Util.GetVar",    new Vector2(100, 200));
        var getMul  = AddNode(graph, catalog, "Util.GetVar",    new Vector2(100, 320));
        var mul     = AddNode(graph, catalog, "Math.Multiply",  new Vector2(320, 200));
        var add     = AddNode(graph, catalog, "Math.Add",       new Vector2(500, 200));
        var clamp   = AddNode(graph, catalog, "Math.Clamp",     new Vector2(700, 200));

        LinkNodes(graph, getBase, 0, mul,   0);
        LinkNodes(graph, getMul,  0, mul,   1);
        LinkNodes(graph, mul,     0, add,   0);
        LinkNodes(graph, add,     0, clamp, 0);
    }
}
