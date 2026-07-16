using System;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.BTree.Editor.Tests.Host;
using Hrot.Editor.AiShared.Blackboard;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Persistence;

/// <summary>
/// E2: proves that placing a Blueprint AiPrimitive palette node via
/// <see cref="BTreeCommandSink.Apply"/> (GraphCommand.AddNode) produces a
/// <see cref="BehaviorTreeAsset"/> that maps — via the save path,
/// <see cref="BehaviorTreeAssetMapper.ToDto"/> — to a persisted DTO Action node equivalent to
/// the committed golden reference
/// Assets/BTrees/Authoring/T31_ComposedAiPrimitive.btree.json: DelegateShape=AiPrimitiveTickCore,
/// WorkingStateTypeId set to the generated WorkingState FQN, ExpressionTargetField set, and a
/// matching Params-typed blackboard variable present in the DTO's Blackboard block.
/// Slice 1 (shared working-state): also proves the sink now auto-creates a SECOND blackboard
/// variable (Role=State, Scope=Node by default) distinct from the Params variable, and binds it via
/// Action.WorkingStateTargetField — the authorable slot that a designer can flip to Scope=Behavior
/// to share the WorkingState with another composed node.
/// </summary>
public sealed class AiPrimitivePlacementRoundTripTests
{
    /// <summary>Stand-in for a Blueprint-compiler-generated AiPrimitive class (mirrors the real
    /// DemoAiPrimitiveNodes used by T31): nests Params (the schema DtoType) and WorkingState.</summary>
    public static class FakeBpGenerated
    {
        public struct Params { public int RunsNeeded; }
        public struct WorkingState { public int Ticks; }
    }

    private static BehaviorTreeBlob EmptyBlob() => new()
    {
        TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
        MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
        IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
    };

    [Fact]
    public void PlacedAiPrimitiveNode_MapsToDto_MatchingT31Shape()
    {
        const string fqn = "Hrot.AI.Behaviors.Generated.Demo_1A2B3C4D_Bp.TickCore";
        var fake = new FakeActionSchemaExporter();
        fake.Seed(fqn, new ActionSchemaEntry(
            fqn, typeof(FakeBpGenerated.Params), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(), "PlacedAiPrimitiveTree", "/t.cs", true,
            "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            "Fdp.Toolkit.Behavior.BTreeContext",
            EmptyBlob());

        var graph  = new StubGraphModel();
        var sink   = new BTreeCommandSink(asset, graph, fake);
        var nodeId = NodeId.NewId();

        asset.IsBlackboardEditorManaged.Should().BeFalse(
            "a freshly-created tree starts with an unmanaged blackboard");

        sink.Apply(new GraphCommand.AddNode(
            nodeId,
            new NodeKindKey("bt.leaf.action::" + fqn),
            Vector2.Zero,
            null));

        // Placing a composed AiPrimitive node hard-requires a managed blackboard (BTREE0002 otherwise),
        // so the sink must enable managed mode itself rather than leaving the first Full Rebuild to fail.
        asset.IsBlackboardEditorManaged.Should().BeTrue(
            "placing a composed AiPrimitive node must auto-enable the editor-managed blackboard");

        // Round-trip through the model → DTO mapper (the save path).
        var dto = BehaviorTreeAssetMapper.ToDto(asset);

        dto.Blackboard.Managed.Should().BeTrue(
            "the persisted DTO must carry Managed=true so codegen binds the AiPrimitive action (no BTREE0002)");

        var actionNodeDto = dto.Nodes.OfType<BTreeActionNodeDto>().Should().ContainSingle().Which;
        actionNodeDto.Action.Should().NotBeNull();
        actionNodeDto.Action!.MethodFqn.Should().Be(fqn);
        actionNodeDto.Action.DelegateShape.Should().Be(BTreeDelegateShapeDto.AiPrimitiveTickCore,
            "matches T31's persisted Action.DelegateShape");
        actionNodeDto.Action.WorkingStateTypeId.Should().Be(
            typeof(FakeBpGenerated.WorkingState).FullName,
            "matches T31's persisted Action.WorkingStateTypeId (the generated WorkingState FQN)");
        actionNodeDto.Action.ExpressionTargetField.Should().NotBeNullOrEmpty(
            "matches T31's persisted Action.ExpressionTargetField (bound to the Params variable)");

        // Slice 1: a SECOND variable (WorkingState, Role=State) must now be auto-created and bound
        // via WorkingStateTargetField, distinct from the Params (Input) variable above.
        actionNodeDto.Action.WorkingStateTargetField.Should().NotBeNullOrEmpty(
            "Slice 1: the sink must auto-create and bind a WorkingState host variable");
        actionNodeDto.Action.WorkingStateTargetField.Should().NotBe(
            actionNodeDto.Action.ExpressionTargetField,
            "the WorkingState variable must be distinct from the Params variable so its Scope is independently authorable");

        dto.Blackboard.Variables.Should().HaveCount(2,
            "Slice 1: placing a composed AiPrimitive action now creates both the bpParams (Input) and bpWorkingState (State) variables");

        var paramsVarDto = dto.Blackboard.Variables.Should().ContainSingle(
            v => v.Name == actionNodeDto.Action.ExpressionTargetField).Which;
        paramsVarDto.Type.TypeId.Should().Be(typeof(FakeBpGenerated.Params).FullName,
            "matches T31's Blackboard.Variables[0].Type.TypeId (the generated Params FQN)");
        paramsVarDto.Role.Should().Be(BlackboardVariableRole.Input,
            "the Params variable stays Input role (unchanged from pre-Slice-1 behavior)");

        var wsVarDto = dto.Blackboard.Variables.Should().ContainSingle(
            v => v.Name == actionNodeDto.Action.WorkingStateTargetField).Which;
        wsVarDto.Type.TypeId.Should().Be(typeof(FakeBpGenerated.WorkingState).FullName,
            "the WorkingState variable is typed as the blueprint's generated WorkingState struct");
        wsVarDto.Role.Should().Be(BlackboardVariableRole.State,
            "the WorkingState variable must be Role=State so its Scope is authorable in the Role/Scope panel");
        wsVarDto.Scope.Should().Be(WorkingStateScope.Node,
            "default scope is Node (private) — matching pre-Slice-1 semantics until a designer opts into Behavior scope");
    }

    /// <summary>
    /// Slice-1 authorability gap fix: the Blackboard Variables panel's Node-Owned Allocations
    /// table now exposes an editable Scope dropdown for auto-managed State rows (see
    /// VariablesPanelControl.DrawNodeOwnedTable), wired to the same
    /// <see cref="IBlackboardManagedAsset.UpdateVariableScope"/> model call as the main table.
    /// This proves the model-level half of that fix: flipping the auto-created bpWorkingState
    /// variable's Scope from Node to Behavior via <see cref="BehaviorTreeAsset.UpdateVariableScope"/>
    /// (exactly what the panel's new Scope combo invokes) persists through the same
    /// model → DTO save path proven above, with the variable remaining IsAutoManaged/Role=State.
    /// The codegen consequence of a Behavior-scoped shared slot (two composed nodes bound to the
    /// same variable sharing one slot) is already proven end-to-end by
    /// Hrot.AiEditor.Generators.Tests.Demos.T35_SharedWorkingState_ProofTests.
    /// </summary>
    [Fact]
    public void PlacedAiPrimitiveNode_WorkingStateScope_FlipToBehavior_PersistsThroughRoundTrip()
    {
        const string fqn = "Hrot.AI.Behaviors.Generated.Demo_1A2B3C4D_Bp.TickCore";
        var fake = new FakeActionSchemaExporter();
        fake.Seed(fqn, new ActionSchemaEntry(
            fqn, typeof(FakeBpGenerated.Params), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(), "PlacedAiPrimitiveTree", "/t.cs", true,
            "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            "Fdp.Toolkit.Behavior.BTreeContext",
            EmptyBlob());

        var graph  = new StubGraphModel();
        var sink   = new BTreeCommandSink(asset, graph, fake);
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId,
            new NodeKindKey("bt.leaf.action::" + fqn),
            Vector2.Zero,
            null));

        var dtoBefore = BehaviorTreeAssetMapper.ToDto(asset);
        var actionBefore = dtoBefore.Nodes.OfType<BTreeActionNodeDto>().Should().ContainSingle().Which;
        string wsVarName = actionBefore.Action!.WorkingStateTargetField!;

        var wsVarBefore = dtoBefore.Blackboard.Variables.Should().ContainSingle(v => v.Name == wsVarName).Which;
        wsVarBefore.Scope.Should().Be(WorkingStateScope.Node, "starts at the pre-Slice-1 default scope");

        // This is exactly the model call the panel's new node-owned Scope dropdown issues
        // (VariablesPanelControl.DrawNodeOwnedTable -> IVariablesSchemaSource.UpdateVariableScope
        // -> BehaviorTreeAsset.UpdateVariableScope). No IsAutoManaged gate blocks it.
        asset.UpdateVariableScope(wsVarName, WorkingStateScope.Behavior);

        var dtoAfter = BehaviorTreeAssetMapper.ToDto(asset);
        var wsVarAfter = dtoAfter.Blackboard.Variables.Should().ContainSingle(v => v.Name == wsVarName).Which;

        wsVarAfter.Scope.Should().Be(WorkingStateScope.Behavior,
            "Slice 1: the Scope flip must survive the model → DTO round-trip, enabling shared " +
            "working-state between composed nodes bound to the same variable");
        wsVarAfter.Role.Should().Be(BlackboardVariableRole.State,
            "Role must remain State -- this fix authors Scope only, never Role, for node-owned rows");

        var actionAfter = dtoAfter.Nodes.OfType<BTreeActionNodeDto>().Should().ContainSingle().Which;
        actionAfter.Action!.WorkingStateTargetField.Should().Be(wsVarName,
            "the node's binding to the WorkingState variable is unaffected by the scope change");
    }
}
