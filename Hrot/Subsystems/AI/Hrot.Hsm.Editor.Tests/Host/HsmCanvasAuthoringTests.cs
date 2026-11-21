using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// CE-1000 / CE-1001 — the HSM side of canvas authoring (docs/blueprints/DESIGN_Hsm_Canvas_Authoring.md):
/// node-to-node routing, the pin a whole-state link uses, unique names, the canvas rename, and the state picker an
/// arrow dropped on empty canvas opens.
/// </summary>
public sealed class HsmCanvasAuthoringTests
{
    private static (HsmAsset asset, HsmCommandSink sink) Build()
    {
        var asset = new HsmAsset(
            Guid.NewGuid(), "Test", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            new StateNode("__root__"),
            new List<StateNode>(), new List<TransitionNode>(), new List<GlobalTransitionNode>(),
            new List<RegionNode>(), new List<EventDefinition>());
        return (asset, new HsmCommandSink(asset));
    }

    private static Guid Add(HsmCommandSink sink, string kind = HsmKinds.Simple)
    {
        var id = Guid.NewGuid();
        sink.Apply(new GraphCommand.AddNode(new NodeId(id), new NodeKindKey(kind), Vector2.Zero, null));
        return id;
    }

    [Fact]
    public void CE1000_TheHsmGraph_RoutesNodeToNode_AndCallsALinkATransition()
    {
        var (asset, _) = Build();
        var model = new HsmGraphModel(asset);
        Assert.Equal(LinkRouting.NodeToNode, model.Kind.Routing);
        Assert.Equal("Transition", model.Kind.LinkDisplayName);
    }

    [Fact]
    public void CE1001_NodeLinkPin_AnswersForAStateThatDoesNotExistYet()
    {
        // The empty-drop batch adds the state and the transition in one step, so the target pin must be known
        // before the state is created — and must be the pin the state will actually have.
        var (asset, sink) = Build();
        IGraphModel model = new HsmGraphModel(asset);
        var future = Guid.NewGuid();
        var predicted = model.NodeLinkPin(new NodeId(future), PinDirection.Input);

        sink.Apply(new GraphCommand.AddNode(new NodeId(future), new NodeKindKey(HsmKinds.Simple), Vector2.Zero, null));
        var actual = asset.FindStateByStableId(future)!.Pins.Single(p => p.Direction == PinDirection.Input).Id;
        Assert.Equal(actual, predicted);
    }

    [Fact]
    public void CE1001_NewStates_GetUniqueNames()
    {
        var (asset, sink) = Build();
        var a = Add(sink); var b = Add(sink); var c = Add(sink);
        Assert.Equal("State", asset.FindStateByStableId(a)!.Name);
        Assert.Equal("State 2", asset.FindStateByStableId(b)!.Name);
        Assert.Equal("State 3", asset.FindStateByStableId(c)!.Name);
    }

    [Fact]
    public void CE1001_TheCanvasRename_RenamesTheState_AndRefusesAnEmptyName()
    {
        var (asset, sink) = Build();
        var a = Add(sink);
        sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(a), "Title", "  Patrol "));
        Assert.Equal("Patrol", asset.FindStateByStableId(a)!.Name);

        sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(a), "Title", "   "));
        Assert.Equal("Patrol", asset.FindStateByStableId(a)!.Name);
    }

    [Fact]
    public void CE1001_RenamingIntoAClash_IsADuplicateStateNameError()
    {
        var (asset, sink) = Build();
        var a = Add(sink); var b = Add(sink);
        Assert.DoesNotContain(new HsmValidator().Validate(asset), d => d.Code == HsmDiagnosticCode.DuplicateStateName);

        sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(b), "Title", "State"));
        var dup = Assert.Single(new HsmValidator().Validate(asset), d => d.Code == HsmDiagnosticCode.DuplicateStateName);
        Assert.Equal(HsmDiagnosticSeverity.Error, dup.Severity);
        Assert.Contains(a, dup.TargetStableIds);
        Assert.Contains(b, dup.TargetStableIds);
    }

    [Fact]
    public void CE1001_ADroppedTransition_OffersStates_SimpleFirst_AndNoHistoryPseudoStates()
    {
        var catalog = new HsmNodeCatalog();
        var offered = catalog.QueryForPinContext(new PinContextQuery(
            new PinId(Guid.NewGuid()), PinDirection.Output, PinKind.Data, null, ""));
        Assert.NotEmpty(offered);
        Assert.Equal(HsmKinds.Simple, offered[0].Kind.Id);
        Assert.DoesNotContain(offered, e => e.Kind.Id == HsmKinds.History || e.Kind.Id == HsmKinds.DeepHistory);
    }
}
