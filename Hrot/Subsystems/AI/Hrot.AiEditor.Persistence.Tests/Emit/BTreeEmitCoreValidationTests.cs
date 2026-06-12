using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Emit;

/// <summary>
/// BATCH-12: Tests for <see cref="BTreeEmitCore"/> validation of unbound nodes.
///
/// Verifies:
/// - EmitTopologyCore throws InvalidOperationException when a reachable Action/Condition is unbound.
/// - EmitTopologyCore does NOT throw when an unbound node is disconnected (not reachable from entry).
/// </summary>
public sealed class BTreeEmitCoreValidationTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static BehaviorTreeAssetDto CreateDtoWithReachableUnboundAction()
    {
        var actionId = new Guid("20000000-0000-0000-0000-000000000002");
        return new BehaviorTreeAssetDto
        {
            AssetId = new Guid("10000000-0000-0000-0000-000000000001"),
            Name = "UnboundActionTest",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = null, // unbound — no payload
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
    }

    private static BehaviorTreeAssetDto CreateDtoWithReachableUnboundCondition()
    {
        var condId = new Guid("30000000-0000-0000-0000-000000000003");
        return new BehaviorTreeAssetDto
        {
            AssetId = new Guid("10000000-0000-0000-0000-000000000001"),
            Name = "UnboundConditionTest",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeConditionNodeDto
                {
                    VisualId = condId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Condition = null, // unbound — no payload
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
    }

    private static BehaviorTreeAssetDto CreateDtoWithReachableAction_EmptyMethodFqn()
    {
        var actionId = new Guid("20000000-0000-0000-0000-000000000002");
        return new BehaviorTreeAssetDto
        {
            AssetId = new Guid("10000000-0000-0000-0000-000000000001"),
            Name = "EmptyMethodActionTest",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn = "", // empty string — effectively unbound
                        DelegateShape = BTreeDelegateShapeDto.FourParamFull,
                    },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
    }

    private static BehaviorTreeAssetDto CreateDtoWithDisconnectedUnboundAction()
    {
        var rootId = new Guid("10000000-0000-0000-0000-000000000001");
        var childId = new Guid("10000000-0000-0000-0000-000000000002");
        var disconnectedId = new Guid("20000000-0000-0000-0000-000000000003");
        return new BehaviorTreeAssetDto
        {
            AssetId = new Guid("aaaa0000-0000-0000-0000-000000000001"),
            Name = "DisconnectedUnboundTest",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                // The entry node — a root pointing to a valid Wait child
                new BTreeRootNodeDto
                {
                    VisualId = rootId,
                    ChildVisualIds = new List<Guid> { childId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                // Reachable child — a valid Wait node
                new BTreeWaitNodeDto
                {
                    VisualId = childId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Wait = new BTreeWaitPayloadDto { Duration = 1.0f },
                },
                // Disconnected unbound Action — NOT in any parent's ChildVisualIds
                new BTreeActionNodeDto
                {
                    VisualId = disconnectedId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = null, // unbound but disconnected → should NOT throw
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
    }

    // ── Tests: reachable unbound → throw ────────────────────────────────────────

    [Fact]
    public void EmitTopologyCore_ReachableUnboundAction_ThrowsInvalidOperationException()
    {
        var dto = CreateDtoWithReachableUnboundAction();

        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Action node*unbound*no method*bind a method in the editor*");
    }

    [Fact]
    public void EmitTopologyCore_ReachableUnboundCondition_ThrowsInvalidOperationException()
    {
        var dto = CreateDtoWithReachableUnboundCondition();

        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Condition node*unbound*no method*bind a method in the editor*");
    }

    [Fact]
    public void EmitTopologyCore_ReachableAction_EmptyMethodFqn_ThrowsInvalidOperationException()
    {
        var dto = CreateDtoWithReachableAction_EmptyMethodFqn();

        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Action node*unbound*");
    }

    // ── Tests: disconnected unbound → does NOT throw ────────────────────────────

    [Fact]
    public void EmitTopologyCore_DisconnectedUnboundAction_DoesNotThrow()
    {
        var dto = CreateDtoWithDisconnectedUnboundAction();

        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        act.Should().NotThrow(
            "a disconnected unbound node is not emitted, so it should not cause a throw");
    }

    // ── Tests: valid nodes still work ────────────────────────────────────────────

    [Fact]
    public void EmitTopologyCore_ReachableBoundAction_DoesNotThrow()
    {
        var actionId = new Guid("20000000-0000-0000-0000-000000000002");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("10000000-0000-0000-0000-000000000001"),
            Name = "BoundActionTest",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn = "Test.Ns.Methods.MyAction",
                        DelegateShape = BTreeDelegateShapeDto.FourParamFull,
                    },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };

        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        act.Should().NotThrow("a reachable bound Action with a valid MethodFqn should emit successfully");
    }

    // ── BATCH-14: cycle detection ─────────────────────────────────────────────────

    [Fact]
    public void EmitTopologyCore_CyclicTree_ThrowsInvalidOperationException_NotStackOverflow()
    {
        // Arrange: Root → A(Sequence) → B(Sequence) → A  (cycle A→B→A)
        var rootId = new Guid("C1000000-0000-0000-0000-000000000001");
        var aId    = new Guid("C1000000-0000-0000-0000-000000000002");
        var bId    = new Guid("C1000000-0000-0000-0000-000000000003");

        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("CC000000-0000-0000-0000-000000000001"),
            Name = "CyclicTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto
                {
                    VisualId = rootId,
                    ChildVisualIds = new List<Guid> { aId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = aId,
                    ChildVisualIds = new List<Guid> { bId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = bId,
                    ChildVisualIds = new List<Guid> { aId }, // cycle! B → A
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };

        // Act
        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        // Assert: must throw InvalidOperationException (catchable), NOT StackOverflowException (uncatchable)
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cycle detected*")
            .Which.Should().NotBeNull("the exception must be a normal, catchable exception — never a stack overflow");
    }

    [Fact]
    public void EmitTopologyCore_SelfChild_Throws()
    {
        // Arrange: a Sequence whose ChildVisualIds contains itself
        var aId = new Guid("D1000000-0000-0000-0000-000000000001");

        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("DD000000-0000-0000-0000-000000000001"),
            Name = "SelfChild",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeSequenceNodeDto
                {
                    VisualId = aId,
                    ChildVisualIds = new List<Guid> { aId }, // self-loop
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };

        // Act
        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cycle detected*");
    }

    [Fact]
    public void EmitTopologyCore_CyclicTree_NoRoot_ThrowsInvalidOperationException()
    {
        // Arrange: no Root node — entry = dto.Nodes[0]
        // A(Sequence) → B(Sequence) → A  (cycle A→B→A)
        var aId = new Guid("E1000000-0000-0000-0000-000000000001");
        var bId = new Guid("E1000000-0000-0000-0000-000000000002");

        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("EE000000-0000-0000-0000-000000000001"),
            Name = "CyclicNoRoot",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeSequenceNodeDto
                {
                    VisualId = aId,
                    ChildVisualIds = new List<Guid> { bId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = bId,
                    ChildVisualIds = new List<Guid> { aId }, // cycle!
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };

        // Act
        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cycle detected*");
    }

    [Fact]
    public void EmitTopologyCore_AcyclicTree_DoesNotThrow()
    {
        // Arrange: a normal Root → Sequence → (Wait, Action-bound) tree — no cycles
        var rootId   = new Guid("F1000000-0000-0000-0000-000000000001");
        var seqId    = new Guid("F1000000-0000-0000-0000-000000000002");
        var waitId   = new Guid("F1000000-0000-0000-0000-000000000003");
        var actionId = new Guid("F1000000-0000-0000-0000-000000000004");

        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("FF000000-0000-0000-0000-000000000001"),
            Name = "AcyclicTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto
                {
                    VisualId = rootId,
                    ChildVisualIds = new List<Guid> { seqId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = seqId,
                    ChildVisualIds = new List<Guid> { waitId, actionId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeWaitNodeDto
                {
                    VisualId = waitId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Wait = new BTreeWaitPayloadDto { Duration = 1.0f },
                },
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn = "Test.Ns.Methods.MyAction",
                        DelegateShape = BTreeDelegateShapeDto.FourParamFull,
                    },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };

        // Act
        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        // Assert
        act.Should().NotThrow("an acyclic tree must emit without any exception");

        // Also verify the output contains expected content
        string result = BTreeEmitCore.EmitTopologyCore(dto);
        result.Should().Contain("CreateBuilder()");
    }

    [Fact]
    public void EmitTopologyCore_DiamondNotACycle_DoesNotThrow()
    {
        // Arrange: a DAG where node D is referenced from two parents (B and C)
        // but no back-edge on any path (no cycle).
        // Root → A(Sequence) → B(Sequence) → D(Wait)
        //                    → C(Sequence) → D(Wait)  (same D, different paths — DAG)
        var rootId = new Guid("D1000000-0000-0000-0000-000000000001");
        var aId    = new Guid("D1000000-0000-0000-0000-000000000002");
        var bId    = new Guid("D1000000-0000-0000-0000-000000000003");
        var cId    = new Guid("D1000000-0000-0000-0000-000000000004");
        var dId    = new Guid("D1000000-0000-0000-0000-000000000005");

        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("DD000000-0000-0000-0000-000000000002"),
            Name = "DiamondTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto
                {
                    VisualId = rootId,
                    ChildVisualIds = new List<Guid> { aId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = aId,
                    ChildVisualIds = new List<Guid> { bId, cId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = bId,
                    ChildVisualIds = new List<Guid> { dId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = cId,
                    ChildVisualIds = new List<Guid> { dId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeWaitNodeDto
                {
                    VisualId = dId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Wait = new BTreeWaitPayloadDto { Duration = 1.0f },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };

        // Act
        Action act = () => BTreeEmitCore.EmitTopologyCore(dto);

        // Assert: diamond/DAG with no back-edge must NOT throw
        act.Should().NotThrow(
            "a DAG (shared child, no back-edges) is not a cycle and must emit successfully");

        // Also verify output is produced
        string result = BTreeEmitCore.EmitTopologyCore(dto);
        result.Should().NotBeNullOrWhiteSpace();
    }
}
