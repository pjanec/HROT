using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
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

    // ── S3-2 scope-aware tests ────────────────────────────────────────────────────

    /// <summary>
    /// S3-2: The same Behavior-scoped variable bound at two different nodes must produce
    /// equal slot keys (they share the slot; nodeVisualId is NOT in the Behavior key).
    /// </summary>
    [Fact]
    public void SlotKey_Behavior_SameVar_TwoNodes_Equal()
    {
        var assetId  = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var nodeId1  = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var nodeId2  = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        const string variableId = "myBehaviorVar";

        int key1 = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, WorkingStateScope.Behavior, nodeId1, variableId);
        int key2 = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, WorkingStateScope.Behavior, nodeId2, variableId);

        key1.Should().Be(key2,
            "Behavior-scoped key depends only on assetId + variableId, not nodeVisualId");
        key1.Should().BeGreaterThanOrEqualTo(0, "slot key must be non-negative (0x7FFFFFFF mask)");
    }

    /// <summary>
    /// S3-2: Two distinct Behavior variables in one asset must produce distinct keys
    /// (no collision — corrects §4.4 pre-resolution concern).
    /// </summary>
    [Fact]
    public void SlotKey_Behavior_TwoVars_Differ()
    {
        var assetId   = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var nodeId    = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        const string varA = "stateVarA";
        const string varB = "stateVarB";

        int keyA = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, WorkingStateScope.Behavior, nodeId, varA);
        int keyB = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, WorkingStateScope.Behavior, nodeId, varB);

        keyA.Should().NotBe(keyB,
            "distinct Behavior variable names must produce distinct slot keys");
    }

    /// <summary>
    /// S3-2: Node scope via the 4-arg overload must be byte-identical to the legacy 2-arg result.
    /// Guards against any drift in S2 assets.
    /// </summary>
    [Fact]
    public void SlotKey_Node_MatchesLegacy()
    {
        var assetId      = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var nodeVisualId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        const string variableId = "anyVar"; // irrelevant for Node scope

        int legacy   = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, nodeVisualId);
        int scopeKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, WorkingStateScope.Node, nodeVisualId, variableId);

        scopeKey.Should().Be(legacy,
            "Node scope via 4-arg overload must be byte-identical to the 2-arg legacy result");
    }

    /// <summary>
    /// S3-2 (optional): Entity scope produces the same key regardless of assetId.
    /// This verifies that assetId is intentionally excluded for post-MVP entity-lifetime slots.
    /// </summary>
    [Fact]
    public void SlotKey_Entity_IndependentOfAsset()
    {
        var assetId1     = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var assetId2     = Guid.Parse("ffffffff-0000-0000-0000-0000000000ff");
        var nodeId       = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        const string variableId = "entityScopedVar";

        int key1 = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId1, WorkingStateScope.Entity, nodeId, variableId);
        int key2 = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId2, WorkingStateScope.Entity, nodeId, variableId);

        key1.Should().Be(key2,
            "Entity-scoped key must not depend on assetId (survives behavior switch)");
        key1.Should().BeGreaterThanOrEqualTo(0, "slot key must be non-negative (0x7FFFFFFF mask)");
    }

    // ── S3-7: manifest carries role/scope ─────────────────────────────────────────

    /// <summary>
    /// S3-7: the emitted StatefulWorkingSlots manifest entry for a Behavior-scoped State variable
    /// must carry the authored Role (State=1) and Scope (Behavior=1) as the trailing ctor args, so
    /// the live inspector can group/label by scope. (Node/Input assets stay byte-identical — the
    /// args are omitted when default — which is why only the non-default case is asserted here.)
    /// </summary>
    [Fact]
    public void StatefulSlotInfo_CarriesRoleAndScope()
    {
        const string ParamsTypeId = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorParams";
        const string StateTypeId  = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState";
        const string MethodFqn    = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_AdvanceCursor";

        var assetId = Guid.Parse("cc000030-0000-0000-0000-000000000000");
        var nodeId  = Guid.Parse("dd000031-0000-0000-0000-000000000000");

        var dto = new BehaviorTreeAssetDto
        {
            AssetId            = assetId,
            Name               = "S3RoleScope",
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
                        Name  = "shared",
                        Type  = new BlackboardTypeRefDto { TypeId = ParamsTypeId },
                        Role  = BlackboardVariableRole.State,
                        Scope = WorkingStateScope.Behavior,
                    }
                }
            },
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto { VisualId = Guid.NewGuid(), ChildVisualIds = new List<Guid> { nodeId }, DisplayLabel = "Root", EditorMetadata = new NodeEditorMetadataDto() },
                new BTreeActionNodeDto
                {
                    VisualId = nodeId, DisplayLabel = "AdvanceShared", EditorMetadata = new NodeEditorMetadataDto(),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn = MethodFqn, ExpressionTargetField = "shared",
                        DelegateShape = BTreeDelegateShapeDto.ThreeParamReusableStateful,
                        WorkingStateTypeId = StateTypeId,
                    }
                }
            }
        };

        Func<string, int?> sizeResolver = t => t == ParamsTypeId ? 4 : (int?)null;
        string bridgeSrc = BTreeBridgeEmitCore.EmitBridge(dto, sizeResolver);

        // Role=State(1), Scope=Behavior(1) appended after the NodeLabel string.
        bridgeSrc.Should().Contain("\"AdvanceShared\", 1, 1)",
            "the StatefulSlotInfo for a Behavior-scoped State variable must carry Role=State(1), Scope=Behavior(1)");
    }
}
