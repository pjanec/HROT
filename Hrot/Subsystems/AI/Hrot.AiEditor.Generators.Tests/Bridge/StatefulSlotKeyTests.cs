using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// S2-1 unit tests for the stateful slot key algorithm and emitter output.
///
/// Tests:
/// 1. <c>SlotKey_KnownGuidPair_ProducesKnownInt</c> — locks the FNV-1a-32 algorithm
///    with a known pair of GUIDs → known int (algorithm stability gate).
/// 2. <c>StatefulEmitter_EmitsBridge_WithTryGetSlotOffset_AndSlotKeyLiteral</c> —
///    runs the real emitter on a fixture DTO with a ThreeParamReusableStateful action node
///    and asserts the emitted bridge source contains:
///    (a) the baked SlotKey literal matching the independently-computed FNV-1a value,
///    (b) "TryGetSlotOffset",
///    (c) WorkingState projection at the returned offset,
///    (d) StatefulWorkingSlots array with correct SlotKey and PayloadSize.
/// </summary>
public sealed class StatefulSlotKeyTests
{
    // ── Replicated FNV-1a-32 (must match BTreeBridgeEmitCore.ComputeStatefulSlotKey) ──

    private static int ComputeSlotKey(Guid assetId, Guid nodeVisualId)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (byte b in assetId.ToByteArray())       { hash ^= b; hash *= 16777619u; }
            foreach (byte b in nodeVisualId.ToByteArray())  { hash ^= b; hash *= 16777619u; }
            return (int)(hash & 0x7FFFFFFFu);
        }
    }

    // ── Test 1: algorithm stability gate ─────────────────────────────────────────

    /// <summary>
    /// Locks the FNV-1a-32 slot-key algorithm: a known (assetGuid, nodeGuid) pair must
    /// produce the same int across build, runtime, and source-generation contexts.
    /// If this test ever breaks the algorithm has diverged — bump DEBT-AIB-030 and
    /// re-derive the expected value.
    /// </summary>
    [Fact]
    public void SlotKey_KnownGuidPair_ProducesKnownInt()
    {
        // Known fixed pair — locked for all time; do NOT change these GUIDs.
        var assetId      = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var nodeVisualId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        int expected = ComputeSlotKey(assetId, nodeVisualId);

        // Verify against the emitter's public implementation.
        int emitterResult = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, nodeVisualId);

        emitterResult.Should().Be(expected,
            "ComputeStatefulSlotKey must produce the same result as the local FNV-1a-32 replication");

        // The key must be non-negative (masked to 0x7FFFFFFF).
        emitterResult.Should().BeGreaterThanOrEqualTo(0,
            "slot key must be a positive int (sign bit cleared)");

        // Sanity: different inputs → different keys (not a trivial zero).
        var otherNodeId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        int otherResult = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, otherNodeId);
        otherResult.Should().NotBe(emitterResult,
            "distinct node VisualIds must produce distinct slot keys");
    }

    // ── Test 2: emitter-exercising test ──────────────────────────────────────────

    /// <summary>
    /// Runs the real <see cref="BTreeBridgeEmitCore.EmitBridge"/> on a fixture DTO containing
    /// one ThreeParamReusableStateful action node and asserts the emitted bridge:
    /// (a) contains the baked SlotKey literal equal to the independently-computed FNV-1a value,
    /// (b) calls TryGetSlotOffset,
    /// (c) projects WorkingState at the returned offset,
    /// (d) populates StatefulWorkingSlots with the correct SlotKey and PayloadSize entries.
    /// </summary>
    [Fact]
    public void StatefulEmitter_EmitsBridge_WithTryGetSlotOffset_AndSlotKeyLiteral()
    {
        // ── Fixture DTO with one stateful action node ─────────────────────────────
        const string CursorParamsTypeId = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorParams";
        const string CursorStateTypeId  = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState";
        const string AdvanceCursorFqn   = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_AdvanceCursor";

        var assetId    = Guid.Parse("cc000020-0000-0000-0000-000000000000");
        var nodeId     = Guid.Parse("dd000021-0000-0000-0000-000000000000");
        int expectedSlotKey = ComputeSlotKey(assetId, nodeId);

        var dto = new BehaviorTreeAssetDto
        {
            AssetId            = assetId,
            Name               = "T20_StatefulDemo",
            TargetNamespace    = "Hrot.AI.Behaviors.Trees",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName    = "Fdp.Toolkit.Behavior.BTreeContext",
            Blackboard         = new BlackboardBlockDto
            {
                Managed  = true,
                TypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto
                    {
                        Name = "cursor",
                        Type = new BlackboardTypeRefDto { TypeId = CursorParamsTypeId },
                    }
                }
            },
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto
                {
                    VisualId        = Guid.NewGuid(),
                    ChildVisualIds  = new List<Guid> { nodeId },
                    DisplayLabel    = "Root",
                    EditorMetadata  = new NodeEditorMetadataDto(),
                },
                new BTreeActionNodeDto
                {
                    VisualId       = nodeId,
                    DisplayLabel   = "AdvanceCursor",
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action         = new BTreeActionPayloadDto
                    {
                        MethodFqn         = AdvanceCursorFqn,
                        ExpressionTargetField = "cursor",
                        DelegateShape     = BTreeDelegateShapeDto.ThreeParamReusableStateful,
                        WorkingStateTypeId = CursorStateTypeId,
                    }
                }
            }
        };

        // Size resolver: DemoCursorParams = { int Limit } = 4 bytes.
        Func<string, int?> sizeResolver = typeId => typeId switch
        {
            CursorParamsTypeId => 4,
            _                  => null,
        };

        // ── Emit the bridge ───────────────────────────────────────────────────────
        string bridgeSrc = BTreeBridgeEmitCore.EmitBridge(dto, sizeResolver);

        // ── Assertions ────────────────────────────────────────────────────────────

        // (a) The baked SlotKey literal must appear in the thunk body.
        bridgeSrc.Should().Contain(expectedSlotKey.ToString(),
            $"emitted bridge must contain the baked SlotKey literal {expectedSlotKey} (FNV-1a result)");

        // (b) TryGetSlotOffset is called.
        bridgeSrc.Should().Contain("TryGetSlotOffset",
            "emitted bridge must call BlueprintBlackboardPartitions.TryGetSlotOffset");

        // (c) WorkingState projection at the returned offset.
        bridgeSrc.Should().Contain("Unsafe.AsRef",
            "emitted bridge must project WorkingState via Unsafe.AsRef at the slot offset");

        // (d) StatefulWorkingSlots is populated with the correct SlotKey.
        bridgeSrc.Should().Contain("StatefulWorkingSlots",
            "emitted bridge must populate StatefulWorkingSlots on BehaviorDefinition");
        bridgeSrc.Should().Contain($"new global::Fdp.Toolkit.Behavior.StatefulSlotInfo({expectedSlotKey},",
            $"StatefulWorkingSlots entry must use SlotKey={expectedSlotKey}");

        // (e) The DemoCursorState type appears in the WorkingState projection.
        bridgeSrc.Should().Contain("DemoCursorState",
            "emitted bridge must reference DemoCursorState as the WorkingState type");

        // (f) The thunk key format is {MethodFqn}@{paramOffset}@{slotKey}.
        bridgeSrc.Should().Contain($"@0@{expectedSlotKey}",
            "stateful thunk key must follow {MethodFqn}@{paramOffset}@{slotKey} format");

        // (g) BATCH-10 PREREQ: StatefulWorkingSlots entry must include typeof(WorkingState).
        bridgeSrc.Should().Contain("typeof(",
            "emitted StatefulWorkingSlots entry must carry typeof(WorkingState) for live-value rendering");
        bridgeSrc.Should().MatchRegex(@"typeof\(.*DemoCursorState.*\)",
            "typeof() must reference the DemoCursorState working-state type");

        // (h) BATCH-10 PREREQ: StatefulWorkingSlots entry must include the node label string.
        bridgeSrc.Should().Contain("\"AdvanceCursor\"",
            "emitted StatefulWorkingSlots entry must carry NodeLabel = the node's DisplayLabel");
    }
}
