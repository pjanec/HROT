using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// I2/I3 emit tests — composing a blueprint-authored AiPrimitive action as a host-BTree node
/// (<see cref="BTreeDelegateShapeDto.AiPrimitiveTickCore"/>).
///
/// Verifies the bridge (<see cref="BTreeBridgeEmitCore"/>) emits, for such a node:
/// 1. a reusable-stateful thunk that projects Params at the baked offset and WorkingState from a
///    <b>partition slot</b> (BlueprintBlackboardPartitions.TryGetSlotOffset — NOT Blackboard1024+8),
///    then dispatches to the blueprint's generated <c>TickCore(ref, ref, self, world, time)</c>;
/// 2. a <c>StatefulWorkingSlots</c> manifest entry for that node's WorkingState (so
///    BehaviorIngressSystem provisions the slot before the first tick);
/// keyed identically to the topology blob key <c>{MethodFqn}@{offset}@{slotKey}</c>.
/// </summary>
public sealed class BlueprintActionThunkEmissionTests
{
    private const string TickCoreFqn = "Hrot.AI.Behaviors.Generated.TestBp_ABCD1234_Bp.TickCore";
    private const string WsTypeId    = "Hrot.AI.Behaviors.Generated.TestBp_ABCD1234_Bp+WorkingState";

    private static BehaviorTreeAssetDto MakeBlueprintActionDto()
    {
        return new BehaviorTreeAssetDto
        {
            AssetId            = Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            Name               = "TestBlueprintCompose",
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
                new BTreeActionNodeDto
                {
                    VisualId = Guid.Parse("dddddddd-0000-0000-0000-0000000000aa"),
                    Action = new BTreeActionPayloadDto
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
    public void BlueprintActionNode_EmitsTickCoreThunk_OverPartitionSlot()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeBlueprintActionDto());

        // Thunk registered under the interpreter's string key: {TickCoreFqn}@{offset}@{slotKey}.
        bridge.Should().Contain($"actionRegistry.Register(\"{TickCoreFqn}@0@",
            "the blueprint action must register a thunk keyed {MethodFqn}@{offset}@{slotKey}");

        // WorkingState comes from the partition-slot rail, not the fixed Blackboard1024+8.
        bridge.Should().Contain("BlueprintBlackboardPartitions.TryGetSlotOffset",
            "WorkingState must be projected from the entity's partition slot (I3), not Blackboard1024+8");

        // The final dispatch calls the blueprint's generated TickCore with the (params, ws, self,
        // world, time) signature — NOT the 4-param node-method shape.
        bridge.Should().Contain(
            $"global::{TickCoreFqn}(ref dto, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime)",
            "the node must dispatch to the blueprint's generated TickCore (I2)");
    }

    [Fact]
    public void BlueprintActionNode_EmitsStatefulWorkingSlotManifest()
    {
        var bridge = BTreeBridgeEmitCore.EmitBridge(MakeBlueprintActionDto());

        // A composed blueprint action rides the partition-slot rail, so it contributes a manifest
        // entry that BehaviorIngressSystem provisions before the first tick.
        bridge.Should().Contain("StatefulWorkingSlots = new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]",
            "a composed blueprint action must emit a StatefulWorkingSlots manifest");
        bridge.Should().Contain("TestBp_ABCD1234_Bp.WorkingState",
            "the manifest entry must reference the blueprint's WorkingState type");
    }
}
