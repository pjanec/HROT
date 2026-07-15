using System;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using Fbt;
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
/// E2: condition-side mirror of <see cref="AiPrimitivePlacementRoundTripTests"/>. Proves that placing
/// a Blueprint AiPrimitive CONDITION palette node via <see cref="BTreeCommandSink.Apply"/>
/// (GraphCommand.AddNode) produces a <see cref="BehaviorTreeAsset"/> that maps — via the save path,
/// <see cref="BehaviorTreeAssetMapper.ToDto"/> — to a persisted DTO Condition node with
/// DelegateShape=AiPrimitiveTickCore, WorkingStateTypeId set to the generated WorkingState FQN,
/// ExpressionTargetField set, and a matching Params-typed blackboard variable present in the DTO's
/// Blackboard block. A composed condition must get a partition-slot WorkingState exactly like an
/// action (edge-detection/hysteresis need cross-tick memory), so this asserts the SAME shape as the
/// action round-trip test — just for a Condition node placed via the
/// <c>bt.leaf.condition::</c> palette kind prefix (see BTreeKinds.ConditionPrefix /
/// BTreeKinds.TryParseLeafActionKind).
/// </summary>
public sealed class AiPrimitiveConditionPlacementRoundTripTests
{
    /// <summary>Stand-in for a Blueprint-compiler-generated AiPrimitive class (mirrors the real
    /// DemoAiPrimitiveNodes used by T31): nests Params (the schema DtoType) and WorkingState.</summary>
    public static class FakeBpGeneratedCondition
    {
        public struct Params { public int Threshold; }
        public struct WorkingState { public int Ticks; }
    }

    private static BehaviorTreeBlob EmptyBlob() => new()
    {
        TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
        MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
        IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
    };

    [Fact]
    public void PlacedAiPrimitiveConditionNode_MapsToDto_MatchingComposedActionShape()
    {
        const string fqn = "Hrot.AI.Behaviors.Generated.DemoCond_5E6F7A8B_Bp.TickCore";
        var fake = new FakeActionSchemaExporter();
        fake.Seed(fqn, new ActionSchemaEntry(
            fqn, typeof(FakeBpGeneratedCondition.Params), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: true, DtoFields: null, IsAiPrimitive: true));

        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(), "PlacedAiPrimitiveConditionTree", "/t.cs", true,
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
            new NodeKindKey(BTreeKinds.ConditionPrefix + fqn),
            Vector2.Zero,
            null));

        // Placing a composed AiPrimitive condition hard-requires a managed blackboard (same
        // BTREE0002 hard-requirement as the action path), so the sink must enable managed mode
        // itself rather than leaving the first Full Rebuild to fail.
        asset.IsBlackboardEditorManaged.Should().BeTrue(
            "placing a composed AiPrimitive condition node must auto-enable the editor-managed blackboard");

        // Round-trip through the model → DTO mapper (the save path).
        var dto = BehaviorTreeAssetMapper.ToDto(asset);

        dto.Blackboard.Managed.Should().BeTrue(
            "the persisted DTO must carry Managed=true so codegen binds the AiPrimitive condition (no BTREE0002)");

        var conditionNodeDto = dto.Nodes.OfType<BTreeConditionNodeDto>().Should().ContainSingle().Which;
        conditionNodeDto.Condition.Should().NotBeNull();
        conditionNodeDto.Condition!.MethodFqn.Should().Be(fqn);
        conditionNodeDto.Condition.DelegateShape.Should().Be(BTreeDelegateShapeDto.AiPrimitiveTickCore,
            "matches the composed-action shape's persisted Condition.DelegateShape");
        conditionNodeDto.Condition.WorkingStateTypeId.Should().Be(
            typeof(FakeBpGeneratedCondition.WorkingState).FullName,
            "matches the composed-action shape's persisted Condition.WorkingStateTypeId (the generated WorkingState FQN) — " +
            "a composed condition must get a partition-slot WorkingState exactly like an action");
        conditionNodeDto.Condition.ExpressionTargetField.Should().NotBeNullOrEmpty(
            "matches the composed-action shape's persisted Condition.ExpressionTargetField (bound to the Params variable)");

        var varDto = dto.Blackboard.Variables.Should().ContainSingle().Which;
        varDto.Name.Should().Be(conditionNodeDto.Condition.ExpressionTargetField);
        varDto.Type.TypeId.Should().Be(typeof(FakeBpGeneratedCondition.Params).FullName,
            "matches the composed-action shape's Blackboard.Variables[0].Type.TypeId (the generated Params FQN)");
    }
}
