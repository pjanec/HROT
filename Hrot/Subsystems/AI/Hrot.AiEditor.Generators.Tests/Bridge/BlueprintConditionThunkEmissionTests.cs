using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// E2 emit tests — composing a blueprint-authored AiPrimitive CONDITION as a host-BTree node
/// (<see cref="BTreeDelegateShapeDto.AiPrimitiveTickCore"/>). Condition-side mirror of
/// <see cref="BlueprintActionThunkEmissionTests"/>.
///
/// Verifies the bridge (<see cref="BTreeBridgeEmitCore"/>) emits, for such a node:
/// 1. a reusable-stateful thunk registered via <c>RegisterCondition</c> that projects Params at the
///    baked offset and WorkingState from a <b>partition slot</b>
///    (BlueprintBlackboardPartitions.TryGetSlotOffset — NOT Blackboard1024+8) — a composed condition
///    needs the SAME cross-tick WorkingState memory as an action (edge-detection/hysteresis), never a
///    transient/zeroed state — then dispatches to the blueprint's generated
///    <c>TickCore(ref, ref, self, world, time)</c> and compares the result against
///    <see cref="Fbt.NodeStatus.Success"/>;
/// 2. a <c>StatefulWorkingSlots</c> manifest entry for that node's WorkingState (so
///    BehaviorIngressSystem provisions the slot before the first tick);
/// keyed identically to the topology blob key <c>{MethodFqn}@{offset}@{slotKey}</c>.
/// </summary>
public sealed class BlueprintConditionThunkEmissionTests
{
    private const string TickCoreFqn = "Hrot.AI.Behaviors.Generated.TestCondBp_EF012345_Bp.TickCore";
    private const string WsTypeId    = "Hrot.AI.Behaviors.Generated.TestCondBp_EF012345_Bp+WorkingState";

    private static BehaviorTreeAssetDto MakeBlueprintConditionDto()
    {
        return new BehaviorTreeAssetDto
        {
            AssetId            = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
            Name               = "TestBlueprintComposeCondition",
            TargetNamespace    = "Test.Ns",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName    = "Fdp.Toolkit.Behavior.BTreeContext",
            Blackboard = new BlackboardBlockDto
            {
                Managed   = true,
                TypeName  = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
                // The host owns the params region; a blittable Int32 stands in for the blueprint's
                // Params here (this test asserts emitted source text, not a compile of the DTO type).
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto
                    {
                        Name = "bpParams",
                        Type = new BlackboardTypeRefDto { TypeId = "System.Int32" },
                    },
                },
            },
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeConditionNodeDto
                {
                    VisualId = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000aa"),
                    Condition = new BTreeConditionPayloadDto
                    {
                        MethodFqn             = TickCoreFqn,
                        ExpressionTargetField = "bpParams",
                        DelegateShape         = BTreeDelegateShapeDto.AiPrimitiveTickCore,
                        WorkingStateTypeId    = WsTypeId,
                    },
                },
            },
        };
    }

    [Fact]
    public void BlueprintConditionNode_EmitsTickCoreThunk_OverPartitionSlot()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeBlueprintConditionDto());

        // Thunk registered under the interpreter's string key: {TickCoreFqn}@{offset}@{slotKey} —
        // via RegisterCondition, not Register.
        bridge.Should().Contain($"actionRegistry.RegisterCondition(\"{TickCoreFqn}@0@",
            "the blueprint condition must register a thunk keyed {MethodFqn}@{offset}@{slotKey}");

        // WorkingState comes from the partition-slot rail, not the fixed Blackboard1024+8 — a
        // composed condition needs the same cross-tick memory as an action.
        bridge.Should().Contain("BlueprintBlackboardPartitions.TryGetSlotOffset",
            "WorkingState must be projected from the entity's partition slot, not Blackboard1024+8");

        // The final dispatch calls the blueprint's generated TickCore with the (params, ws, self,
        // world, time) signature and compares the NodeStatus result against Success.
        bridge.Should().Contain(
            $"global::{TickCoreFqn}(ref dto, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime) == Fbt.NodeStatus.Success",
            "the condition must dispatch to the blueprint's generated TickCore and compare against Success");
    }

    [Fact]
    public void BlueprintConditionNode_EmitsStatefulWorkingSlotManifest()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeBlueprintConditionDto());

        // A composed blueprint condition rides the partition-slot rail exactly like an action, so it
        // contributes a manifest entry that BehaviorIngressSystem provisions before the first tick.
        bridge.Should().Contain("StatefulWorkingSlots = new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]",
            "a composed blueprint condition must emit a StatefulWorkingSlots manifest");
        bridge.Should().Contain("TestCondBp_EF012345_Bp.WorkingState",
            "the manifest entry must reference the blueprint's WorkingState type");
    }
}
