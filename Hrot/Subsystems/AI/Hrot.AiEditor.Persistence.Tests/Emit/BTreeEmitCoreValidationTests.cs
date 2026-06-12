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
}
