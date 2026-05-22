using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S13: Debug visualization mock — breakpoints, executing, recently-executed.</summary>
public sealed class S13_DebugVizMock : Scenario
{
    public override string Name        => "13 — Debug Viz Mock";
    public override string Description => "Click 'Attach Debugger' to start simulated execution. Watch node states change.";

    private FakeDebugSession? _session;

    public override FakeDebugSession? Session => _session;

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        var begin   = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
        var branch  = AddNode(graph, catalog, "Flow.Branch",     new Vector2(350, 200));
        var print1  = AddNode(graph, catalog, "Util.Print",      new Vector2(600, 120));
        var print2  = AddNode(graph, catalog, "Util.Print",      new Vector2(600, 320));
        var delay   = AddNode(graph, catalog, "Flow.Delay",      new Vector2(850, 200));

        LinkNodes(graph, begin,  0, branch, 0);
        LinkNodes(graph, branch, 0, print1, 0);
        LinkNodes(graph, branch, 1, print2, 0);

        // Create debug session cycling through all 5 nodes
        _session = new FakeDebugSession(begin, branch, print1, print2, delay);
        // Mark begin node as having a breakpoint by default
        _session.ToggleBreakpoint(begin);
    }
}
