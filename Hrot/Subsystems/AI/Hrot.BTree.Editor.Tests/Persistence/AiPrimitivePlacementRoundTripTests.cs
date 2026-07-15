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
}
