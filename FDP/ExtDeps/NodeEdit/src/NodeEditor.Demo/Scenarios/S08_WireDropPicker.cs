using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S08: Drop wire on empty canvas → contextual node picker.</summary>
public sealed class S08_WireDropPicker : Scenario
{
    public override string Name        => "08 — Wire Drop Picker";
    public override string Description => "Drag a wire from a pin and release on empty canvas for a contextual picker.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Event.Tick", new Vector2(100, 200));
    }
}
