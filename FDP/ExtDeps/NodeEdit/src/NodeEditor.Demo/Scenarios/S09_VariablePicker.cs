using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S09: Variable picker — pick a variable from the list.</summary>
public sealed class S09_VariablePicker : Scenario
{
    public override string Name        => "09 — Variable Picker";
    public override string Description => "Canvas shows a 'Pick Variable' button. Click it to open a Compact-layout picker.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        AddNode(graph, catalog, "Util.GetVar", new Vector2(200, 200));
        AddNode(graph, catalog, "Util.SetVar", new Vector2(450, 200));
    }
}
