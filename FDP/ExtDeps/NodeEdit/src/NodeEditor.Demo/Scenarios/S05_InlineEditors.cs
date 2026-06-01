using System;
using System.Numerics;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S05: Comprehensive test of all registered inline pin editors.</summary>
public sealed class S05_InlineEditors : Scenario
{
    public override string Name        => "05 — Inline Editors";
    public override string Description => "Comprehensive test of all registered inline pin editors.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // 1. Standard catalog nodes with typical inputs
        var lerp = AddNode(graph, catalog, "Math.Lerp", new Vector2(50, 100));
        var print = AddNode(graph, catalog, "Util.Print", new Vector2(50, 280));
        var andNode = AddNode(graph, catalog, "Logic.And", new Vector2(50, 420));

        InitializeDefaults(graph.FindNode(lerp) as FakeNodeModel, 0.5f);
        InitializeDefaults(graph.FindNode(print) as FakeNodeModel, "Hello World");
        InitializeDefaults(graph.FindNode(andNode) as FakeNodeModel, true);

        // 2. Comprehensive 'Kitchen Sink' Node
        var sinkNodeId = IdGenerator.NewNodeId();
        var sinkNode = graph.AddNode(sinkNodeId, new NodeKindKey("Demo.KitchenSink"), "All Editors Test", new Vector2(400, 100));

        AddTestPin(sinkNode, "Boolean", "System.Boolean", false);
        AddTestPin(sinkNode, "Integer", "System.Int32", 42);
        AddTestPin(sinkNode, "Float", "System.Single", 3.141f);
        AddTestPin(sinkNode, "String", "System.String", "Test String");
        AddTestPin(sinkNode, "Vector 2", "System.Numerics.Vector2", new Vector2(1f, 2f));
        AddTestPin(sinkNode, "Vector 3", "System.Numerics.Vector3", new Vector3(1f, 2f, 3f));
        AddTestPin(sinkNode, "Vector 4", "System.Numerics.Vector4", new Vector4(1f, 2f, 3f, 4f));
        AddTestPin(sinkNode, "Quaternion", "System.Numerics.Quaternion", Quaternion.Identity);
        AddTestPin(sinkNode, "Color", "NodeEditor.Color", new Vector4(1f, 0.5f, 0.2f, 1f));
        AddTestPin(sinkNode, "Guid", "System.Guid", Guid.NewGuid());

        // Add an output pin to demonstrate visual separation (no editor expected here)
        sinkNode.AddPin("Output", PinDirection.Output, PinKind.Data, new TypeKey("System.Single"));
    }

    private static void InitializeDefaults(FakeNodeModel? node, object defaultValue)
    {
        if (node == null) return;
        foreach (var p in node.Pins)
        {
            if (p.Direction == PinDirection.Input && p.Kind == PinKind.Data)
                ((FakePinModel)p).Default = new FakePinDefaultValue(defaultValue);
        }
    }

    private static void AddTestPin(FakeNodeModel node, string label, string typeId, object defaultValue)
    {
        var pin = node.AddPin(label, PinDirection.Input, PinKind.Data, new TypeKey(typeId));
        pin.Default = new FakePinDefaultValue(defaultValue);
    }
}
