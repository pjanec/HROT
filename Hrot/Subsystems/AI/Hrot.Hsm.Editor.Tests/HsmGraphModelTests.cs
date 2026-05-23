using System;
using System.Linq;
using FluentAssertions;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public class HsmGraphModelTests
{
    // ---- helpers ----

    private static (HsmDefinitionBlob blob, MachineMetadata metadata) Compile(HsmBuilder builder)
    {
        var graph = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return (blob, metadata);
    }

    private static HsmAsset Project(HsmDefinitionBlob blob, MachineMetadata metadata,
        string name = "TestMachine")
    {
        return HsmAssetProjector.Project(
            blob, metadata, null,
            Guid.NewGuid(), name, "", false, "");
    }

    // ---- container node tests (HS-S1-09, 10, 11) ----

    [Fact]
    public void Simple_state_IsContainer_false()
    {
        var state = new StateNode("S");
        state.IsContainer.Should().BeFalse();
    }

    [Fact]
    public void State_with_children_IsContainer_true()
    {
        var state = new StateNode("Parent");
        state.Children.Add(new StateNode("Child"));
        state.IsContainer.Should().BeTrue();
    }

    [Fact]
    public void Parallel_state_IsContainer_true()
    {
        var state = new StateNode("Par");
        state.IsParallel = true;
        state.IsContainer.Should().BeTrue();
    }

    [Fact]
    public void State_Id_wraps_StableId()
    {
        var state = new StateNode("S");
        state.Id.Value.Should().Be(state.StableId);
    }

    [Fact]
    public void State_Kind_simple_when_no_children()
    {
        var state = new StateNode("S");
        state.Kind.Id.Should().Be(HsmKinds.Simple);
    }

    [Fact]
    public void State_Kind_composite_when_has_children()
    {
        var state = new StateNode("Parent");
        state.Children.Add(new StateNode("Child"));
        state.Kind.Id.Should().Be(HsmKinds.Composite);
    }

    [Fact]
    public void State_Kind_parallel()
    {
        var state = new StateNode("Par");
        state.IsParallel = true;
        state.Kind.Id.Should().Be(HsmKinds.Parallel);
    }

    [Fact]
    public void State_Kind_final()
    {
        var state = new StateNode("F");
        state.IsFinal = true;
        state.Kind.Id.Should().Be(HsmKinds.Final);
    }

    [Fact]
    public void State_Kind_history()
    {
        var state = new StateNode("H");
        state.IsHistory = true;
        state.Kind.Id.Should().Be(HsmKinds.History);
    }

    [Fact]
    public void State_Kind_deepHistory()
    {
        var state = new StateNode("DH");
        state.IsDeepHistory = true;
        state.Kind.Id.Should().Be(HsmKinds.DeepHistory);
    }

    [Fact]
    public void State_ChildNodeIds_match_children()
    {
        var parent = new StateNode("Parent");
        var c1 = new StateNode("C1");
        var c2 = new StateNode("C2");
        parent.Children.Add(c1);
        parent.Children.Add(c2);

        var ids = parent.ChildNodeIds;
        ids.Should().Contain(new NodeId(c1.StableId));
        ids.Should().Contain(new NodeId(c2.StableId));
        ids.Should().HaveCount(2);
    }

    [Fact]
    public void Top_level_state_ParentContainerId_is_null()
    {
        // root -> topLevel; root has no parent
        var root = new StateNode("__root__");
        var topLevel = new StateNode("Top");
        topLevel.Parent = root;

        topLevel.ParentContainerId.Should().BeNull();
    }

    [Fact]
    public void Nested_state_ParentContainerId_set()
    {
        // root -> composite -> child
        var root = new StateNode("__root__");
        var composite = new StateNode("Composite");
        composite.Parent = root;
        var child = new StateNode("Child");
        child.Parent = composite;

        child.ParentContainerId.Should().Be(new NodeId(composite.StableId));
    }

    [Fact]
    public void State_Pins_count_is_two()
    {
        var state = new StateNode("S");
        state.Pins.Should().HaveCount(2);
    }

    [Fact]
    public void State_output_pin_Id_matches_HiddenOutputPinId()
    {
        var state = new StateNode("S");
        state.Pins[0].Id.Should().Be(new PinId(state.HiddenOutputPinId));
    }

    [Fact]
    public void State_input_pin_Id_matches_HiddenInputPinId()
    {
        var state = new StateNode("S");
        state.Pins[1].Id.Should().Be(new PinId(state.HiddenInputPinId));
    }

    // ---- HsmTransitionLink tests (HS-S1-12) ----

    [Fact]
    public void TransitionLink_Id_equals_VisualId()
    {
        var (src, tgt, tn) = MakeTransition();
        var link = new HsmTransitionLink(tn);
        link.Id.Value.Should().Be(tn.VisualId);
    }

    [Fact]
    public void TransitionLink_FromPin_equals_source_output()
    {
        var (src, tgt, tn) = MakeTransition();
        var link = new HsmTransitionLink(tn);
        link.FromPin.Value.Should().Be(src.HiddenOutputPinId);
    }

    [Fact]
    public void TransitionLink_ToPin_equals_target_input()
    {
        var (src, tgt, tn) = MakeTransition();
        var link = new HsmTransitionLink(tn);
        link.ToPin.Value.Should().Be(tgt.HiddenInputPinId);
    }

    [Fact]
    public void TransitionLink_external_is_Solid()
    {
        var (_, _, tn) = MakeTransition(TransitionKind.External);
        var link = new HsmTransitionLink(tn);
        link.Style.Should().Be(LinkStyle.Solid);
    }

    [Fact]
    public void TransitionLink_internal_is_Dashed()
    {
        var (_, _, tn) = MakeTransition(TransitionKind.Internal);
        var link = new HsmTransitionLink(tn);
        link.Style.Should().Be(LinkStyle.Dashed);
    }

    // ---- HsmGraphModel tests ----

    [Fact]
    public void GraphModel_Nodes_contains_all_states()
    {
        var asset = BuildSimpleAsset();
        var model = new HsmGraphModel(asset);
        model.Nodes.Count.Should().Be(asset.AllStates.Count);
    }

    [Fact]
    public void GraphModel_Links_contains_all_transitions()
    {
        var asset = BuildSimpleAsset();
        var model = new HsmGraphModel(asset);
        model.Links.Count.Should().Be(asset.AllTransitions.Count);
    }

    [Fact]
    public void GraphModel_FindNode_returns_state()
    {
        var asset = BuildSimpleAsset();
        var model = new HsmGraphModel(asset);
        var state = asset.AllStates.First();
        model.FindNode(new NodeId(state.StableId)).Should().NotBeNull();
    }

    [Fact]
    public void GraphModel_FindPin_finds_output_pin()
    {
        var asset = BuildSimpleAsset();
        var model = new HsmGraphModel(asset);
        var state = asset.AllStates.First();
        model.FindPin(new PinId(state.HiddenOutputPinId)).Should().NotBeNull();
    }

    [Fact]
    public void GraphModel_FindLink_finds_transition()
    {
        var asset = BuildAssetWithTransition();
        var model = new HsmGraphModel(asset);
        var t = asset.AllTransitions.First();
        model.FindLink(new LinkId(t.VisualId)).Should().NotBeNull();
    }

    // ---- private helpers ----

    private static (StateNode src, StateNode tgt, TransitionNode tn) MakeTransition(
        TransitionKind kind = TransitionKind.External)
    {
        var src = new StateNode("Src");
        var tgt = new StateNode("Tgt");
        var tn = new TransitionNode
        {
            VisualId = Guid.NewGuid(),
            Source = src,
            Target = tgt,
            Kind = kind,
        };
        return (src, tgt, tn);
    }

    private static HsmAsset BuildSimpleAsset()
    {
        var builder = new HsmBuilder("Simple");
        builder.State("Idle").Initial();
        var (blob, metadata) = Compile(builder);
        return Project(blob, metadata, "Simple");
    }

    private static HsmAsset BuildAssetWithTransition()
    {
        var builder = new HsmBuilder("WithTrans");
        builder.Event("Trigger", 1);
        builder.State("Active").Final();
        builder.State("Idle").Initial().On("Trigger").GoTo("Active");
        var (blob, metadata) = Compile(builder);
        return Project(blob, metadata, "WithTrans");
    }
}
