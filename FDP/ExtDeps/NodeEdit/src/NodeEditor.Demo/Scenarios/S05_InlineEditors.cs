using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S05: A node with inline bool/int/float/string pin defaults.</summary>
public sealed class S05_InlineEditors : Scenario
{
    public override string Name        => "05 — Inline Editors";
    public override string Description => "Node with bool/int/float/string default-value mini-editors.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Use a math node that has float inputs (those will show inline editors when disconnected)
        var lerp = AddNode(graph, catalog, "Math.Lerp",    new Vector2(200, 200));
        AddNode(graph, catalog, "Math.Clamp",   new Vector2(500, 200));
        AddNode(graph, catalog, "Math.Add",     new Vector2(200, 380));

        // Set default values on pins so they show the mini-editor
        var lerpNode = (FakeNodeModel)graph.FindNode(lerp)!;
        foreach (var pin in lerpNode.Pins.Where(p => p.Direction == PinDirection.Input && p.Kind == PinKind.Data))
            ((FakePinModel)pin).Default = new FakePinDefaultValue(0.5f);
    }
}
