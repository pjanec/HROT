using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Layout;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Tests for BT-S1-02: BehaviorTreeAssetProjector.
/// </summary>
public sealed class BehaviorTreeAssetProjectionTests
{
    // ---- Helpers ------------------------------------------------------------

    private static NodeDefinition N(NodeType type, byte childCount, ushort subtreeOffset, int payloadIndex = 0) =>
        new NodeDefinition { Type = type, ChildCount = childCount, SubtreeOffset = subtreeOffset, RawPayloadIndex = payloadIndex };

    private static BehaviorTreeBlob SimpleBlob(
        NodeDefinition[] nodes,
        string[]? methodNames = null,
        float[]? floatParams = null,
        int[]? intParams = null,
        string[]? subtreeAssetIds = null) =>
        new BehaviorTreeBlob
        {
            TreeName        = "Test",
            Nodes           = nodes,
            MethodNames     = methodNames     ?? Array.Empty<string>(),
            FloatParams     = floatParams     ?? Array.Empty<float>(),
            IntParams       = intParams       ?? Array.Empty<int>(),
            SubtreeAssetIds = subtreeAssetIds ?? Array.Empty<string>(),
        };

    // Root(Sequence(Action1, Action2))
    // Index 0: Root     (ChildCount=1, SubtreeOffset=4)
    // Index 1: Sequence (ChildCount=2, SubtreeOffset=3)
    // Index 2: Action1  (ChildCount=0, SubtreeOffset=1, PayloadIndex=0)
    // Index 3: Action2  (ChildCount=0, SubtreeOffset=1, PayloadIndex=1)
    private static BehaviorTreeBlob RootSequence2Actions() =>
        SimpleBlob(
            new[]
            {
                N(NodeType.Root,     1, 4),
                N(NodeType.Sequence, 2, 3),
                N(NodeType.Action,   0, 1, 0),
                N(NodeType.Action,   0, 1, 1),
            },
            methodNames: new[] { "Ns.Class.Action1", "Ns.Class.Action2" });

    // Root(Inverter(Action))
    // Index 0: Root    (ChildCount=1, SubtreeOffset=3)
    // Index 1: Inverter(ChildCount=1, SubtreeOffset=2)
    // Index 2: Action  (ChildCount=0, SubtreeOffset=1)
    private static BehaviorTreeBlob RootInverterAction() =>
        SimpleBlob(
            new[]
            {
                N(NodeType.Root,     1, 3),
                N(NodeType.Inverter, 1, 2),
                N(NodeType.Action,   0, 1, 0),
            },
            methodNames: new[] { "Ns.Class.DoThing" });

    // Root(Cooldown(Repeater(Sequence(Action))))
    // Index 0: Root     (ChildCount=1, SubtreeOffset=5)
    // Index 1: Cooldown (ChildCount=1, SubtreeOffset=4, PayloadIndex=0) -> FloatParams[0]
    // Index 2: Repeater (ChildCount=1, SubtreeOffset=3, PayloadIndex=0) -> IntParams[0]
    // Index 3: Sequence (ChildCount=1, SubtreeOffset=2)
    // Index 4: Action   (ChildCount=0, SubtreeOffset=1, PayloadIndex=0) -> MethodNames[0]
    private static BehaviorTreeBlob RootCooldownRepeaterSequenceAction() =>
        SimpleBlob(
            new[]
            {
                N(NodeType.Root,     1, 5),
                N(NodeType.Cooldown, 1, 4, 0),
                N(NodeType.Repeater, 1, 3, 0),
                N(NodeType.Sequence, 1, 2),
                N(NodeType.Action,   0, 1, 0),
            },
            methodNames: new[] { "DoThing" },
            floatParams: new[] { 2.5f },
            intParams:   new[] { 3 });

    private static BehaviorTreeAsset Project(BehaviorTreeBlob blob,
        NodeDebugMetadata[]? meta = null, BTreeEditorLayout? layout = null) =>
        BehaviorTreeAssetProjector.Project(
            blob, meta, layout,
            Guid.NewGuid(), "TestTree", string.Empty, false,
            string.Empty, string.Empty);

    // ---- Tests --------------------------------------------------------------

    [Fact]
    public void Project_simple_tree_creates_correct_node_count()
    {
        var asset = Project(RootSequence2Actions());

        asset.Nodes.Count.Should().Be(4);
        asset.Pills.Count.Should().Be(0);
    }

    [Fact]
    public void Project_assigns_visual_ids_from_debug_metadata()
    {
        var rootGuid = new Guid("AAAAAAAA-0000-0000-0000-000000000001");
        var seqGuid  = new Guid("BBBBBBBB-0000-0000-0000-000000000002");
        var act1Guid = new Guid("CCCCCCCC-0000-0000-0000-000000000003");
        var act2Guid = new Guid("DDDDDDDD-0000-0000-0000-000000000004");

        var meta = new[]
        {
            new NodeDebugMetadata { VisualId = rootGuid.ToString("D"), Label = "Root" },
            new NodeDebugMetadata { VisualId = seqGuid.ToString("D"),  Label = "Seq"  },
            new NodeDebugMetadata { VisualId = act1Guid.ToString("D"), Label = "A1"   },
            new NodeDebugMetadata { VisualId = act2Guid.ToString("D"), Label = "A2"   },
        };

        var asset = Project(RootSequence2Actions(), meta);

        asset.Nodes[0].VisualId.Should().Be(rootGuid);
        asset.Nodes[1].VisualId.Should().Be(seqGuid);
        asset.Nodes[2].VisualId.Should().Be(act1Guid);
        asset.Nodes[3].VisualId.Should().Be(act2Guid);
    }

    [Fact]
    public void Project_mints_fresh_guids_when_no_debug_metadata()
    {
        var asset = Project(RootSequence2Actions(), meta: null);

        foreach (var node in asset.Nodes)
        {
            node.VisualId.Should().NotBe(Guid.Empty);
        }
    }

    [Fact]
    public void Project_builds_correct_parent_child_hierarchy()
    {
        var asset = Project(RootSequence2Actions());

        var root   = asset.Nodes[0];
        var seq    = asset.Nodes[1];
        var action1 = asset.Nodes[2];
        var action2 = asset.Nodes[3];

        root.KernelType.Should().Be(NodeType.Root);
        seq.KernelType.Should().Be(NodeType.Sequence);

        root.ChildVisualIds.Should().ContainSingle()
            .Which.Should().Be(seq.VisualId);
        seq.ChildVisualIds.Should().HaveCount(2)
            .And.Contain(action1.VisualId)
            .And.Contain(action2.VisualId);
    }

    [Fact]
    public void Project_single_decorator_creates_one_pill()
    {
        var asset = Project(RootInverterAction());

        asset.Nodes.Count.Should().Be(2,
            "Root and Action become editor nodes; Inverter becomes a pill");
        asset.Pills.Count.Should().Be(1);

        asset.Pills[0].DecoratorType.Should().Be(NodeType.Inverter);
    }

    [Fact]
    public void Project_two_decorators_create_two_pills_with_correct_stack_indices()
    {
        var asset = Project(RootCooldownRepeaterSequenceAction());

        asset.Pills.Count.Should().Be(2);

        var cooldownPill  = asset.Pills[0];
        var repeaterPill  = asset.Pills[1];

        cooldownPill.DecoratorType.Should().Be(NodeType.Cooldown);
        repeaterPill.DecoratorType.Should().Be(NodeType.Repeater);

        // Cooldown is outermost => StackIndex = 1
        cooldownPill.StackIndex.Should().Be(1);
        // Repeater is innermost => StackIndex = 0
        repeaterPill.StackIndex.Should().Be(0);
    }

    [Fact]
    public void Project_decorator_pill_points_to_host_node()
    {
        var asset = Project(RootCooldownRepeaterSequenceAction());

        // Sequence (index 3 in blob) is the host.
        var sequence = asset.FindNode(asset.Nodes[1].VisualId);
        sequence.Should().NotBeNull();
        sequence!.KernelType.Should().Be(NodeType.Sequence);

        foreach (var pill in asset.Pills)
        {
            pill.HostNodeVisualId.Should().Be(sequence.VisualId,
                "both pills must point to the Sequence node as host");
        }
    }

    [Fact]
    public void Project_applies_layout_positions_when_provided()
    {
        var rootGuid = new Guid("EEEEEEEE-0000-0000-0000-000000000001");
        var meta = new[]
        {
            new NodeDebugMetadata { VisualId = rootGuid.ToString("D") },
            new NodeDebugMetadata { VisualId = string.Empty },
            new NodeDebugMetadata { VisualId = string.Empty },
            new NodeDebugMetadata { VisualId = string.Empty },
        };

        var expectedPos = new Vector2(123f, 456f);
        var layoutDict = new Dictionary<Guid, NodeLayoutEntry>
        {
            [rootGuid] = new NodeLayoutEntry { Position = expectedPos },
        };
        var layout = new BTreeEditorLayout
        {
            PanOffset = new Vector2(10f, 20f),
            ZoomLevel = 1.5f,
            Nodes     = layoutDict,
        };

        var asset = Project(RootSequence2Actions(), meta, layout);

        var root = asset.Nodes[0];
        root.VisualId.Should().Be(rootGuid);
        root.Position.Should().Be(expectedPos);

        asset.CanvasPanOffset.Should().Be(new Vector2(10f, 20f));
        asset.CanvasZoomLevel.Should().BeApproximately(1.5f, 0.001f);
    }

    [Fact]
    public void Project_sets_action_payload_from_blob()
    {
        var blob = SimpleBlob(
            new[]
            {
                N(NodeType.Root,   1, 2),
                N(NodeType.Action, 0, 1, 0),
            },
            methodNames: new[] { "DoThing" });

        var asset = Project(blob);

        var actionNode = asset.Nodes[1];
        actionNode.KernelType.Should().Be(NodeType.Action);
        actionNode.Action.Should().NotBeNull();
        actionNode.Action!.MethodFqn.Should().Be("DoThing");
    }

    [Fact]
    public void Project_sets_wait_payload_duration_from_blob()
    {
        var blob = SimpleBlob(
            new[]
            {
                N(NodeType.Root, 1, 2),
                N(NodeType.Wait, 0, 1, 0),
            },
            floatParams: new[] { 3.5f });

        var asset = Project(blob);

        var waitNode = asset.Nodes[1];
        waitNode.KernelType.Should().Be(NodeType.Wait);
        waitNode.Wait.Should().NotBeNull();
        waitNode.Wait!.Duration.Should().BeApproximately(3.5f, 0.001f);
    }

    // ---- Sync bindings projection tests (1e-03) ----

    [Fact]
    public void Project_LoadsSyncBindings_FromLayout()
    {
        var nodeId = Guid.NewGuid();
        var binding = new SubtreeSyncBinding("HP", "MasterHP", SyncIn: true, SyncOut: false);
        var layout = new BTreeEditorLayoutBuilder()
            .Node(nodeId.ToString("D"), new Vector2(0, 0))
            .SubtreeSyncField(nodeId.ToString("D"), "HP", "MasterHP", syncIn: true, syncOut: false)
            .Build();

        var asset = Project(SimpleBlob(new[] { N(NodeType.Root, 0, 1) }), layout: layout);

        var bindings = asset.GetAllSyncBindings();
        bindings.Should().ContainKey(nodeId);
        bindings[nodeId].Should().ContainSingle(b => b.FieldName == "HP" && b.SyncIn);
    }

    [Fact]
    public void Project_SyncBindings_EmptyWhenLayoutHasNone()
    {
        var layout = new BTreeEditorLayoutBuilder()
            .Canvas(Vector2.Zero, 1.0f)
            .Build();

        var asset = Project(SimpleBlob(new[] { N(NodeType.Root, 0, 1) }), layout: layout);

        var bindings = asset.GetAllSyncBindings();
        Assert.Empty(bindings);
    }

    [Fact]
    public void Project_SyncBindings_EmptyWhenLayoutIsNull()
    {
        var asset = Project(SimpleBlob(new[] { N(NodeType.Root, 0, 1) }));

        var bindings = asset.GetAllSyncBindings();
        Assert.Empty(bindings);
    }

    [Fact]
    public void Project_PreservesMultipleSyncBindings_PerNode()
    {
        var nodeId = Guid.NewGuid();
        var layout = new BTreeEditorLayoutBuilder()
            .SubtreeSyncField(nodeId.ToString("D"), "Aim",   "MasterAim",   syncIn: true,  syncOut: false)
            .SubtreeSyncField(nodeId.ToString("D"), "Speed", "MasterSpeed", syncIn: false, syncOut: true)
            .Build();

        var asset = Project(SimpleBlob(new[] { N(NodeType.Root, 0, 1) }), layout: layout);

        var bindings = asset.GetAllSyncBindings();
        bindings.Should().ContainKey(nodeId);
        bindings[nodeId].Should().HaveCount(2);
    }
}

// ---- BTreeEditorLayoutBuilder SubtreeSyncField tests ----

public sealed class BTreeEditorLayoutBuilderSyncTests
{
    [Fact]
    public void SubtreeSyncField_StoredInLayout_SingleBinding()
    {
        var nodeId = Guid.NewGuid();
        var layout = new BTreeEditorLayoutBuilder()
            .SubtreeSyncField(nodeId.ToString("D"), "Aim", "MasterAim", syncIn: true, syncOut: false)
            .Build();

        layout.SyncBindings.Should().ContainKey(nodeId);
        layout.SyncBindings[nodeId].Should().ContainSingle(b =>
            b.FieldName == "Aim" && b.MasterVariableName == "MasterAim" && b.SyncIn && !b.SyncOut);
    }

    [Fact]
    public void SubtreeSyncField_AccumulatesMultipleBindings_SameNode()
    {
        var nodeId = Guid.NewGuid();
        var layout = new BTreeEditorLayoutBuilder()
            .SubtreeSyncField(nodeId.ToString("D"), "A", "MA", syncIn: true,  syncOut: false)
            .SubtreeSyncField(nodeId.ToString("D"), "B", "MB", syncIn: false, syncOut: true)
            .Build();

        layout.SyncBindings[nodeId].Should().HaveCount(2);
    }

    [Fact]
    public void SubtreeSyncField_AccumulatesBindings_DifferentNodes()
    {
        var node1 = Guid.NewGuid();
        var node2 = Guid.NewGuid();
        var layout = new BTreeEditorLayoutBuilder()
            .SubtreeSyncField(node1.ToString("D"), "F1", "M1", syncIn: true,  syncOut: false)
            .SubtreeSyncField(node2.ToString("D"), "F2", "M2", syncIn: false, syncOut: true)
            .Build();

        layout.SyncBindings.Should().ContainKey(node1);
        layout.SyncBindings.Should().ContainKey(node2);
        Assert.Equal(2, layout.SyncBindings.Count);
    }

    [Fact]
    public void SyncBindings_EmptyDictionary_WhenNoneAdded()
    {
        var layout = new BTreeEditorLayoutBuilder()
            .Canvas(System.Numerics.Vector2.Zero, 1.0f)
            .Build();

        Assert.Empty(layout.SyncBindings);
    }

    [Fact]
    public void SubtreeSyncField_NullMasterVar_StoredCorrectly()
    {
        var nodeId = Guid.NewGuid();
        var layout = new BTreeEditorLayoutBuilder()
            .SubtreeSyncField(nodeId.ToString("D"), "Phase", null, syncIn: false, syncOut: false)
            .Build();

        layout.SyncBindings[nodeId].Should().ContainSingle(b =>
            b.FieldName == "Phase" && b.MasterVariableName == null);
    }
}
