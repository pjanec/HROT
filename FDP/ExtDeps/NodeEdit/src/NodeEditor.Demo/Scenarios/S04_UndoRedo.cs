using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S04: Perform operations then Ctrl+Z / Ctrl+Y to undo and redo.</summary>
public sealed class S04_UndoRedo : Scenario
{
    public override string Name        => "04 — Undo / Redo";
    public override string Description => "Move a node, then Ctrl+Z to undo, Ctrl+Y to redo.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100,  200));
        AddNode(graph, catalog, "Util.Print",       new Vector2(350,  200));
        AddNode(graph, catalog, "Math.Add",          new Vector2(350,  350));
    }
}
