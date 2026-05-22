using NodeEditor.Core.Commands;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>Abstract base for all demo scenarios.</summary>
public abstract class Scenario
{
    public abstract string Name        { get; }
    public abstract string Description { get; }

    /// <summary>Populate the given graph view with initial state.</summary>
    public abstract void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog);

    /// <summary>Optional pre-build configuration step for the fake host services.</summary>
    public virtual void Setup(FakeMyBlueprintModel mbModel) { }

    /// <summary>
    /// Override to supply multi-graph setup. Returns null (single-graph) by default.
    /// Only S25 and similar multi-tab scenarios need this.
    /// </summary>
    public virtual FakeGraphContainer? BuildMultiGraph(FakeNodeCatalog catalog, out FakeMyBlueprintModel myBlueprint)
    {
        myBlueprint = null!;
        return null;
    }

    /// <summary>Optional debug session exposed by scenarios that set breakpoints.</summary>
    public virtual FakeDebugSession? Session => null;

    // ── helpers ────────────────────────────────────────────────────────────────

    protected static NodeId AddNode(FakeGraphModel graph, FakeNodeCatalog catalog, string kindId, Vector2 pos)
    {
        var id    = IdGenerator.NewNodeId();
        var entry = catalog.All.First(e => e.Kind.Id == kindId);
        var node  = graph.AddNode(id, entry.Kind, entry.DisplayName, pos);
        foreach (var sig in entry.Inputs)  node.AddPin(sig.Label, PinDirection.Input,  sig.Kind, sig.Type);
        foreach (var sig in entry.Outputs) node.AddPin(sig.Label, PinDirection.Output, sig.Kind, sig.Type);
        return id;
    }

    protected static LinkId LinkNodes(FakeGraphModel graph, NodeId fromNode, int fromPinIndex, NodeId toNode, int toPinIndex)
    {
        var fromN  = (FakeNodeModel)graph.FindNode(fromNode)!;
        var toN    = (FakeNodeModel)graph.FindNode(toNode)!;
        var fromPin = fromN.Pins.Where(p => p.Direction == PinDirection.Output).ElementAt(fromPinIndex);
        var toPin   = toN.Pins.Where(p => p.Direction == PinDirection.Input).ElementAt(toPinIndex);
        var id      = IdGenerator.NewLinkId();
        graph.AddLink(id, fromPin.Id, toPin.Id);
        return id;
    }
}
