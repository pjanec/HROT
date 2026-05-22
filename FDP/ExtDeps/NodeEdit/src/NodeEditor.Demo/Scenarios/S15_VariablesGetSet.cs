using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S15: Variables — pre-populate My Blueprint with example variables and show an empty canvas.</summary>
public sealed class S15_VariablesGetSet : Scenario
{
    public override string Name        => "15 — Variables: Get/Set Drag";
    public override string Description => "In My Blueprint, drag an existing variable onto the canvas. Pick 'Get' or 'Set' from the popup.";

    public override void Setup(FakeMyBlueprintModel mbModel)
    {
        mbModel.AddVariable("var.health2",   "Health",    new Vector4(0.15f, 0.63f, 0.90f, 1f), "Player health (0..100)");
        mbModel.AddVariable("var.position",  "Position",  new Vector4(0.20f, 0.75f, 0.30f, 1f), "World position");
    }

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Empty canvas — the user creates content interactively
    }
}
