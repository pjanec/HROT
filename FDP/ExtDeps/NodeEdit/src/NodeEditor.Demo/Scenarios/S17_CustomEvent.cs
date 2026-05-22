using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S17: Custom Event — create a custom event with parameters in My Blueprint.</summary>
public sealed class S17_CustomEvent : Scenario
{
    public override string Name        => "17 — Custom Event";
    public override string Description => "Click '+' next to 'Events' in My Blueprint. Name it 'OnEnemyKilled', add params, then drag it onto canvas.";

    public override void Setup(FakeMyBlueprintModel mbModel)
    {
        mbModel.AddCustomEvent("evt.enemy_killed", "OnEnemyKilled");
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Empty graph — user places the event call node by dragging from My Blueprint
    }
}
