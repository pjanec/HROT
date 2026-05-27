using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeSyncPersistenceTests
{
    // ---- Helpers ----

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(string name = "MasterAI") =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), name, $"/trees/{name}.cs", true,
            "Hrot.Game.MasterBlackboard", "Hrot.Game.MasterContext",
            EmptyBlob(), "Hrot.AI.Behaviors.Trees");

    // Adds a subtree-type node at the given position (so EmitLayout generates node entries).
    private static Guid AddSubtreeNode(BehaviorTreeAsset asset, Vector2 pos)
    {
        var id = Guid.NewGuid();
        asset.AddNode(new BTreeEditorNode
        {
            VisualId = id,
            KernelType = NodeType.Subtree,
            KernelBlobIndex = 0,
            Position = pos,
            Subtree = new BTreeSubtreePayload { SubtreeAssetId = Guid.NewGuid() },
        });
        return id;
    }

    // ---- T1: EmitLayout includes sync field when binding has SyncIn ----

    [Fact]
    public void EmitLayout_IncludesSyncField_WhenBindingHasSyncIn()
    {
        var asset = MakeAsset();
        var nodeId = AddSubtreeNode(asset, new Vector2(10, 20));
        var binding = new SubtreeSyncBinding("Health", "MasterHealth", SyncIn: true, SyncOut: false);
        asset.LoadSyncBindings(new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>
        {
            [nodeId] = new[] { binding }
        });

        string emitted = new BTreeFluentEmitter().Emit(asset);

        emitted.Should().Contain(".SubtreeSyncField(");
        emitted.Should().Contain("\"Health\"");
        emitted.Should().Contain("\"MasterHealth\"");
        emitted.Should().Contain("syncIn: true");
        emitted.Should().Contain("syncOut: false");
    }

    // ---- T2: EmitLayout omits sync field when binding is all-false and no master var ----

    [Fact]
    public void EmitLayout_OmitsSyncField_WhenBindingIsAllFalseAndNoMasterVar()
    {
        var asset = MakeAsset();
        var nodeId = AddSubtreeNode(asset, new Vector2(0, 0));
        var binding = new SubtreeSyncBinding("Speed", MasterVariableName: null, SyncIn: false, SyncOut: false);
        asset.LoadSyncBindings(new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>
        {
            [nodeId] = new[] { binding }
        });

        string emitted = new BTreeFluentEmitter().Emit(asset);

        emitted.Should().NotContain(".SubtreeSyncField(");
    }

    // ---- T3: EmitLayout emits sync fields in field-name order ----

    [Fact]
    public void EmitLayout_EmitsSyncFields_InFieldNameOrder()
    {
        var asset = MakeAsset();
        var nodeId = AddSubtreeNode(asset, new Vector2(0, 0));
        var bindings = new List<SubtreeSyncBinding>
        {
            new SubtreeSyncBinding("Zeal",  "MasterZeal",  SyncIn: true,  SyncOut: false),
            new SubtreeSyncBinding("Alpha", "MasterAlpha", SyncIn: true,  SyncOut: false),
            new SubtreeSyncBinding("Mana",  "MasterMana",  SyncIn: false, SyncOut: true),
        };
        asset.LoadSyncBindings(new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>
        {
            [nodeId] = bindings
        });

        string emitted = new BTreeFluentEmitter().Emit(asset);

        int posAlpha = emitted.IndexOf("\"Alpha\"", StringComparison.Ordinal);
        int posMana  = emitted.IndexOf("\"Mana\"",  StringComparison.Ordinal);
        int posZeal  = emitted.IndexOf("\"Zeal\"",  StringComparison.Ordinal);

        posAlpha.Should().BePositive();
        posMana.Should().BeGreaterThan(posAlpha);
        posZeal.Should().BeGreaterThan(posMana);
    }

    // ---- T4: LoadSyncBindings restores state after projection ----

    [Fact]
    public void LoadSyncBindings_RestoresState_AfterProjection()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        var bindings = new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>
        {
            [nodeId] = new[]
            {
                new SubtreeSyncBinding("HP", "MasterHp", SyncIn: true,  SyncOut: false),
                new SubtreeSyncBinding("MP", "MasterMp", SyncIn: false, SyncOut: true),
            }
        };

        asset.LoadSyncBindings(bindings);

        var restored = asset.GetAllSyncBindings();
        restored.Should().ContainKey(nodeId);
        restored[nodeId].Should().HaveCount(2);
        restored[nodeId].Select(b => b.FieldName).Should().Contain("HP").And.Contain("MP");
    }

    // ---- T5: LoadSyncBindings with null clears existing bindings ----

    [Fact]
    public void LoadSyncBindings_Null_ClearsExistingBindings()
    {
        var asset = MakeAsset();
        var nodeId = Guid.NewGuid();
        asset.LoadSyncBindings(new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>
        {
            [nodeId] = new[] { new SubtreeSyncBinding("X", null, SyncIn: true, SyncOut: false) }
        });

        // Passing null is a "reset" operation.
        asset.LoadSyncBindings(null);

        var result = asset.GetAllSyncBindings();
        Assert.Empty(result);
    }
}
