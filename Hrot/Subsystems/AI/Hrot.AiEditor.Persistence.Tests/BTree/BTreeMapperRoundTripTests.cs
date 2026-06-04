using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using FluentAssertions;
using Fbt;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.BTree;

/// <summary>
/// PU-102 mapping round-trip tests: model → DTO → model preserves every persisted
/// field per design §5.2.
///
/// Fixtures: SampleScout loaded via reflection (real assembly), and a hand-built
/// asset that exercises all persisted fields (sync bindings, suppressions,
/// blackboard, pills, multiple node types).
/// </summary>
public sealed class BTreeMapperRoundTripTests
{
    // ── Reflection-loaded fixture: SampleScout ────────────────────────────────

    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    private static BehaviorTreeAsset LoadSampleScout()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var assets = contributor.Enumerate();
        return (BehaviorTreeAsset)assets.Should().ContainSingle(a => a.Name == "SampleScout")
            .Which;
    }

    // ── SampleScout round-trip ────────────────────────────────────────────────

    [Fact]
    public void SampleScout_ModelToDto_AssetIdentityPreserved()
    {
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);

        dto.AssetId.Should().Be(model.AssetId, "AssetId must survive model→DTO");
        dto.Name.Should().Be(model.Name,         "Name must survive model→DTO");
    }

    [Fact]
    public void SampleScout_ModelToDtoToModel_AssetIdentityPreserved()
    {
        var original = LoadSampleScout();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        restored.AssetId.Should().Be(original.AssetId);
        restored.Name.Should().Be(original.Name);
        restored.TargetNamespace.Should().Be(original.TargetNamespace);
    }

    [Fact]
    public void SampleScout_ModelToDtoToModel_NodeCountPreserved()
    {
        var original = LoadSampleScout();
        var dto      = BehaviorTreeAssetMapper.ToDto(original);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        // SampleScout has Root + Sequence + Wait1 + Wait2 = 4 nodes
        restored.Nodes.Count.Should().Be(original.Nodes.Count,
            "all nodes must survive the round-trip");
    }

    [Fact]
    public void SampleScout_ModelToDtoToModel_VisualIdsPreserved()
    {
        var original = LoadSampleScout();
        var dto      = BehaviorTreeAssetMapper.ToDto(original);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        for (int i = 0; i < original.Nodes.Count; i++)
        {
            var origNode  = original.Nodes[i];
            var restNode  = restored.FindNode(origNode.VisualId);
            restNode.Should().NotBeNull($"node {origNode.VisualId} must be restorable by VisualId");
        }
    }

    [Fact]
    public void SampleScout_ModelToDtoToModel_CanvasLayoutPreserved()
    {
        var original = LoadSampleScout();
        var dto      = BehaviorTreeAssetMapper.ToDto(original);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        restored.CanvasPanOffset.X.Should().BeApproximately(original.CanvasPanOffset.X, 0.001f);
        restored.CanvasPanOffset.Y.Should().BeApproximately(original.CanvasPanOffset.Y, 0.001f);
        restored.CanvasZoomLevel.Should().BeApproximately(original.CanvasZoomLevel, 0.001f);
    }

    [Fact]
    public void SampleScout_ModelToDtoToModel_NodePositionsPreserved()
    {
        var original = LoadSampleScout();
        var dto      = BehaviorTreeAssetMapper.ToDto(original);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        foreach (var origNode in original.Nodes)
        {
            var restNode = restored.FindNode(origNode.VisualId);
            restNode.Should().NotBeNull();
            restNode!.Position.X.Should().BeApproximately(origNode.Position.X, 0.001f,
                because: $"X position of node {origNode.VisualId} must survive round-trip");
            restNode.Position.Y.Should().BeApproximately(origNode.Position.Y, 0.001f,
                because: $"Y position of node {origNode.VisualId} must survive round-trip");
        }
    }

    [Fact]
    public void SampleScout_ModelToDtoToModel_NodeKernelTypesPreserved()
    {
        var original = LoadSampleScout();
        var dto      = BehaviorTreeAssetMapper.ToDto(original);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        foreach (var origNode in original.Nodes)
        {
            var restNode = restored.FindNode(origNode.VisualId);
            restNode.Should().NotBeNull();
            restNode!.KernelType.Should().Be(origNode.KernelType,
                because: $"KernelType of node {origNode.VisualId} must survive round-trip");
        }
    }

    [Fact]
    public void SampleScout_ModelToDtoToModel_ChildVisualIdsPreserved()
    {
        var original = LoadSampleScout();
        var dto      = BehaviorTreeAssetMapper.ToDto(original);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        foreach (var origNode in original.Nodes)
        {
            var restNode = restored.FindNode(origNode.VisualId);
            restNode.Should().NotBeNull();
            restNode!.ChildVisualIds.Should().BeEquivalentTo(origNode.ChildVisualIds,
                because: $"child links of node {origNode.VisualId} must survive round-trip");
        }
    }

    [Fact]
    public void SampleScout_ModelToDtoToModel_WaitPayloadPreserved()
    {
        var original = LoadSampleScout();
        var dto      = BehaviorTreeAssetMapper.ToDto(original);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        // SampleScout has two Wait nodes; check their durations
        var origWaits = original.Nodes.Where(n => n.KernelType == NodeType.Wait).ToList();
        origWaits.Should().HaveCount(2, "SampleScout has exactly two Wait nodes");

        foreach (var origWait in origWaits)
        {
            var restWait = restored.FindNode(origWait.VisualId);
            restWait.Should().NotBeNull();
            restWait!.Wait.Should().NotBeNull();
            restWait.Wait!.Duration.Should().BeApproximately(origWait.Wait!.Duration, 0.001f,
                because: $"Wait duration of node {origWait.VisualId} must survive round-trip");
        }
    }

    // ── Hand-built comprehensive fixture ─────────────────────────────────────

    private static BehaviorTreeAsset BuildComprehensiveFixture()
    {
        var assetId = new Guid("aabbccdd-1111-2222-3333-444444444444");
        var blob = new BehaviorTreeBlob
        {
            TreeName = "ComprehensiveTree", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };

        var asset = new BehaviorTreeAsset(
            assetId, "ComprehensiveTree", "/tmp/test.btree.json",
            isEditorOwned: true,
            "MyBlackboard", "MyContext", blob, "Hrot.AI.Behaviors.Trees");

        asset.CanvasPanOffset = new Vector2(10f, 20f);
        asset.CanvasZoomLevel = 1.5f;

        var root = new BTreeEditorNode
        {
            VisualId = new Guid("10000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Root,
            Position = new Vector2(0f, 0f),
            DisplayLabel = "Root",
        };
        var sequence = new BTreeEditorNode
        {
            VisualId = new Guid("20000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Sequence,
            Position = new Vector2(200f, 100f),
            DisplayLabel = "MainSeq",
            Comment = "main sequence comment",
        };
        var action = new BTreeEditorNode
        {
            VisualId = new Guid("30000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Action,
            Position = new Vector2(300f, 250f),
            Action = new BTreeActionPayload
            {
                MethodFqn = "Hrot.AI.Behaviors.Brains.TestNodes.Action_Test",
                ExpressionTargetField = "ActiveTarget",
                DelegateShape = BTreeActionDelegateShape.ThreeParamReusable,
            },
        };
        var condition = new BTreeEditorNode
        {
            VisualId = new Guid("40000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Condition,
            Position = new Vector2(100f, 250f),
            Condition = new BTreeConditionPayload
            {
                MethodFqn = "Hrot.AI.Behaviors.Brains.TestNodes.Condition_HasTarget",
                DelegateShape = BTreeActionDelegateShape.FourParamFull,
            },
        };
        var subtree = new BTreeEditorNode
        {
            VisualId = new Guid("50000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Subtree,
            Position = new Vector2(400f, 250f),
            Subtree = new BTreeSubtreePayload
            {
                SubtreeAssetId = new Guid("bbbbcccc-1111-2222-3333-444444444444"),
                SubtreeName = "SubTree",
                IsResolved = true,
            },
        };

        root.ChildVisualIds.Add(sequence.VisualId);
        sequence.ChildVisualIds.Add(condition.VisualId);
        sequence.ChildVisualIds.Add(action.VisualId);
        sequence.ChildVisualIds.Add(subtree.VisualId);

        asset.AddNode(root);
        asset.AddNode(sequence);
        asset.AddNode(action);
        asset.AddNode(condition);
        asset.AddNode(subtree);

        // Add a pill (Inverter on condition node)
        asset.AddPill(new BTreeEditorPill
        {
            VisualId = new Guid("60000000-0000-0000-0000-000000000001"),
            HostNodeVisualId = condition.VisualId,
            DecoratorType = NodeType.Inverter,
            StackIndex = 0,
            Comment = "invert the condition",
        });

        // Add sync binding
        asset.SetSyncBinding(subtree.VisualId, new Hrot.Editor.AiShared.Blackboard.SubtreeSyncBinding(
            "AmmoCount", "SharedAmmo", SyncIn: true, SyncOut: false));

        // Add suppressions
        asset.SetConflictSuppressed("AmmoCount", "node1.vs.node2", true);
        asset.SetUnusedWarningSuppressed("OldField", true);

        // Add blackboard variable
        asset.AddVariable(new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry(
            "AmmoCount", typeof(int), "Bullets remaining"));

        asset.ClearDirty();
        return asset;
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_IdentityPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        restored.AssetId.Should().Be(original.AssetId);
        restored.Name.Should().Be(original.Name);
        restored.TargetNamespace.Should().Be(original.TargetNamespace);
        restored.BlackboardTypeName.Should().Be(original.BlackboardTypeName);
        restored.ContextTypeName.Should().Be(original.ContextTypeName);
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_CanvasPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        restored.CanvasPanOffset.X.Should().BeApproximately(10f, 0.001f);
        restored.CanvasPanOffset.Y.Should().BeApproximately(20f, 0.001f);
        restored.CanvasZoomLevel.Should().BeApproximately(1.5f, 0.001f);
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_AllNodeKernelTypesPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        restored.Nodes.Count.Should().Be(original.Nodes.Count);
        foreach (var origNode in original.Nodes)
        {
            var restNode = restored.FindNode(origNode.VisualId);
            restNode.Should().NotBeNull($"VisualId {origNode.VisualId} must be found");
            restNode!.KernelType.Should().Be(origNode.KernelType);
        }
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_ActionPayloadPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        var origAction = original.Nodes.First(n => n.KernelType == NodeType.Action);
        var restAction = restored.FindNode(origAction.VisualId);

        restAction.Should().NotBeNull();
        restAction!.Action.Should().NotBeNull();
        restAction.Action!.MethodFqn.Should().Be(origAction.Action!.MethodFqn);
        restAction.Action.ExpressionTargetField.Should().Be(origAction.Action.ExpressionTargetField);
        restAction.Action.DelegateShape.Should().Be(origAction.Action.DelegateShape);
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_ConditionPayloadPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        var origCond = original.Nodes.First(n => n.KernelType == NodeType.Condition);
        var restCond = restored.FindNode(origCond.VisualId);

        restCond.Should().NotBeNull();
        restCond!.Condition.Should().NotBeNull();
        restCond.Condition!.MethodFqn.Should().Be(origCond.Condition!.MethodFqn);
        restCond.Condition.DelegateShape.Should().Be(origCond.Condition.DelegateShape);
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_SubtreePayloadPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        var origSub = original.Nodes.First(n => n.KernelType == NodeType.Subtree);
        var restSub = restored.FindNode(origSub.VisualId);

        restSub.Should().NotBeNull();
        restSub!.Subtree.Should().NotBeNull();
        restSub.Subtree!.SubtreeAssetId.Should().Be(origSub.Subtree!.SubtreeAssetId);
        restSub.Subtree.SubtreeName.Should().Be(origSub.Subtree.SubtreeName);
        restSub.Subtree.IsResolved.Should().Be(origSub.Subtree.IsResolved);
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_PillPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        restored.Pills.Count.Should().Be(1, "one pill in fixture");
        var origPill = original.Pills[0];
        var restPill = restored.FindPill(origPill.VisualId);

        restPill.Should().NotBeNull();
        restPill!.HostNodeVisualId.Should().Be(origPill.HostNodeVisualId);
        restPill.DecoratorType.Should().Be(origPill.DecoratorType);
        restPill.StackIndex.Should().Be(origPill.StackIndex);
        restPill.Comment.Should().Be(origPill.Comment);
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_SyncBindingsPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        var subtreeNodeId = original.Nodes.First(n => n.KernelType == NodeType.Subtree).VisualId;
        var origBindings = original.GetSyncBindings(subtreeNodeId);
        var restBindings = restored.GetSyncBindings(subtreeNodeId);

        restBindings.Count.Should().Be(origBindings.Count, "sync binding count must survive round-trip");
        restBindings[0].FieldName.Should().Be(origBindings[0].FieldName);
        restBindings[0].MasterVariableName.Should().Be(origBindings[0].MasterVariableName);
        restBindings[0].SyncIn.Should().Be(origBindings[0].SyncIn);
        restBindings[0].SyncOut.Should().Be(origBindings[0].SyncOut);
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_SuppressionsPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        restored.IsConflictSuppressed("AmmoCount", "node1.vs.node2")
            .Should().BeTrue("conflict suppression must survive round-trip");
        restored.IsUnusedWarningSuppressed("OldField")
            .Should().BeTrue("unused suppression must survive round-trip");
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_BlackboardVariablePreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        var dto = BehaviorTreeAssetMapper.ToDto(original);

        // DTO blackboard block present with expected structure (§5.4)
        dto.Blackboard.Should().NotBeNull();
        dto.Blackboard.Variables.Should().HaveCount(1, "one variable in fixture");

        var varDto = dto.Blackboard.Variables[0];
        varDto.Name.Should().Be("AmmoCount");
        varDto.Type.TypeId.Should().Be(typeof(int).FullName, "TypeId must be CLR full name for int");
        varDto.Comment.Should().Be("Bullets remaining");

        // Round-trip: variable preserved in restored model
        restored.BlackboardVariables.Should().ContainSingle(v => v.Name == "AmmoCount");
        var restoredVar = restored.BlackboardVariables.First(v => v.Name == "AmmoCount");
        restoredVar.Comment.Should().Be("Bullets remaining");
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_NodeCommentsPreserved()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));

        var seqId = new Guid("20000000-0000-0000-0000-000000000001");
        var restSeq = restored.FindNode(seqId);
        restSeq.Should().NotBeNull();
        restSeq!.Comment.Should().Be("main sequence comment", "node comment must survive round-trip");
    }

    [Fact]
    public void Comprehensive_DtoDoesNotContainKernelBlobIndex()
    {
        var original = BuildComprehensiveFixture();
        var dto = BehaviorTreeAssetMapper.ToDto(original);

        // Verify no KernelBlobIndex in any node DTO (it's runtime-only)
        foreach (var nodeDto in dto.Nodes)
        {
            var props = nodeDto.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);
            props.Should().NotContain(p => p.Name == "KernelBlobIndex",
                because: "KernelBlobIndex is runtime-only and excluded per §5.2");
        }
    }

    [Fact]
    public void Restored_IsDirty_IsFalse()
    {
        var original = BuildComprehensiveFixture();
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(original));
        // IsDirty is excluded from DTO (session-only); restored asset should not be dirty
        restored.IsDirty.Should().BeFalse("mapping does not activate dirty state");
    }
}
