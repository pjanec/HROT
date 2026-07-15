using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// Phase D (AIE-053): "Open Blueprint" context-menu item on composed AiPrimitive nodes.
/// Tests the testable core (<see cref="BTreeNodeContextMenuProvider.ResolveOpenBlueprintTarget"/>
/// and <see cref="BTreeNodeContextMenuProvider.GetItemsFor"/>) headlessly; ImGui menu rendering
/// itself is out of scope (see the "UI-only" note in the top-level report).
/// <para>
/// Uses plain-string composed FQNs + a fake blueprint that reports a chosen
/// <see cref="IComposedBlueprintIdentity.GeneratedClassName"/> — no hash/sanitize here.
/// </para>
/// </summary>
public sealed class BTreeNodeContextMenuProviderOpenBlueprintTests
{
    private const string ClassName = "ParamDemo_CEFE162F_Bp";
    private static string ComposedFqn(string className = ClassName, string method = "TickCore") =>
        $"{ComposedBlueprintResolver.GeneratedNamespace}.{className}.{method}";

    // ---- helpers ------------------------------------------------------------

    private sealed class FakeBlueprint : IEditableAsset, IComposedBlueprintIdentity
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Fake";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = string.Empty;
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public string? GeneratedClassName { get; init; }
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets;
        public FakeCatalog(params IEditableAsset[] assets) => _assets = new List<IEditableAsset>(assets);
        public IReadOnlyList<IEditableAsset> All => _assets;
        public IEditableAsset? FindByAssetId(Guid assetId) => _assets.FirstOrDefault(a => a.AssetId == assetId);
        public IEditableAsset? FindByName(string name) => _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
        public event Action<AssetKind>? Changed { add { } remove { } }
    }

    private static BehaviorTreeBlob EmptyBlob() => new BehaviorTreeBlob
    {
        TreeName        = "test",
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(Guid.NewGuid(), "TestTree", "/TestTree.cs", true, "BB", "Ctx", EmptyBlob());

    private static (BehaviorTreeAsset asset, Guid nodeId) AddComposedActionNode(BehaviorTreeAsset asset, string methodFqn)
    {
        var nodeId = Guid.NewGuid();
        var node = new BTreeEditorNode
        {
            VisualId     = nodeId,
            KernelType   = NodeType.Action,
            DisplayLabel = "ComposedAction",
            Action       = new BTreeActionPayload
            {
                MethodFqn     = methodFqn,
                DelegateShape = BTreeActionDelegateShape.AiPrimitiveTickCore,
            },
        };
        asset.ReplaceAll(new List<BTreeEditorNode> { node }, new List<BTreeEditorPill>(), EmptyBlob());
        return (asset, nodeId);
    }

    private static (BehaviorTreeAsset asset, Guid nodeId) AddPlainSequenceNode(BehaviorTreeAsset asset)
    {
        var nodeId = Guid.NewGuid();
        var node = new BTreeEditorNode { VisualId = nodeId, KernelType = NodeType.Sequence };
        asset.ReplaceAll(new List<BTreeEditorNode> { node }, new List<BTreeEditorPill>(), EmptyBlob());
        return (asset, nodeId);
    }

    // ---- ResolveOpenBlueprintTarget -------------------------------------------

    [Fact]
    public void ResolveOpenBlueprintTarget_resolves_composed_action_node_to_its_blueprint()
    {
        var blueprint = new FakeBlueprint { Name = "Param Demo", GeneratedClassName = ClassName };
        var catalog   = new FakeCatalog(blueprint);

        var asset = MakeAsset();
        var (_, nodeId) = AddComposedActionNode(asset, ComposedFqn());

        var model    = new BTreeGraphModel(asset);
        var sink     = new BTreeCommandSink(asset, model);
        var provider = new BTreeNodeContextMenuProvider(sink, model, asset, catalog, _ => { });

        var resolved = provider.ResolveOpenBlueprintTarget(new NodeId(nodeId));

        resolved.Should().BeSameAs(blueprint);
    }

    [Fact]
    public void ResolveOpenBlueprintTarget_returns_null_when_blueprint_reference_is_dangling()
    {
        var asset = MakeAsset();
        var (_, nodeId) = AddComposedActionNode(asset, ComposedFqn());

        var model    = new BTreeGraphModel(asset);
        var sink     = new BTreeCommandSink(asset, model);
        var provider = new BTreeNodeContextMenuProvider(sink, model, asset, new FakeCatalog(), _ => { });

        provider.ResolveOpenBlueprintTarget(new NodeId(nodeId)).Should().BeNull();
    }

    [Fact]
    public void ResolveOpenBlueprintTarget_returns_null_for_non_composed_node()
    {
        var asset = MakeAsset();
        var (_, nodeId) = AddPlainSequenceNode(asset);

        var model    = new BTreeGraphModel(asset);
        var sink     = new BTreeCommandSink(asset, model);
        var provider = new BTreeNodeContextMenuProvider(sink, model, asset, new FakeCatalog(), _ => { });

        provider.ResolveOpenBlueprintTarget(new NodeId(nodeId)).Should().BeNull();
    }

    [Fact]
    public void ResolveOpenBlueprintTarget_returns_null_when_provider_constructed_without_asset_or_catalog()
    {
        // Historical "Add Decorator"-only call sites/tests construct the provider with just
        // (sink, model); the new feature must stay fully opt-in.
        var asset = MakeAsset();
        var (_, nodeId) = AddComposedActionNode(asset, ComposedFqn());

        var model    = new BTreeGraphModel(asset);
        var sink     = new BTreeCommandSink(asset, model);
        var provider = new BTreeNodeContextMenuProvider(sink, model);

        provider.ResolveOpenBlueprintTarget(new NodeId(nodeId)).Should().BeNull();
    }

    // ---- GetItemsFor: "Open Blueprint" menu item ------------------------------

    [Fact]
    public void GetItemsFor_adds_OpenBlueprint_item_for_resolvable_composed_node()
    {
        var blueprint = new FakeBlueprint { Name = "Param Demo", GeneratedClassName = ClassName };
        var catalog   = new FakeCatalog(blueprint);

        var asset = MakeAsset();
        var (_, nodeId) = AddComposedActionNode(asset, ComposedFqn());

        IEditableAsset? opened = null;
        var model    = new BTreeGraphModel(asset);
        var sink     = new BTreeCommandSink(asset, model);
        var provider = new BTreeNodeContextMenuProvider(sink, model, asset, catalog, a => opened = a);

        var items = provider.GetItemsFor(new NodeId(nodeId), new[] { new NodeId(nodeId) });

        items.Should().Contain(i => i.Label == "Open Blueprint");
        items.First(i => i.Label == "Open Blueprint").Execute();

        opened.Should().BeSameAs(blueprint);
    }

    [Fact]
    public void GetItemsFor_omits_OpenBlueprint_item_for_dangling_reference()
    {
        var asset = MakeAsset();
        var (_, nodeId) = AddComposedActionNode(asset, ComposedFqn());

        var model    = new BTreeGraphModel(asset);
        var sink     = new BTreeCommandSink(asset, model);
        var provider = new BTreeNodeContextMenuProvider(sink, model, asset, new FakeCatalog(), _ => { });

        var items = provider.GetItemsFor(new NodeId(nodeId), new[] { new NodeId(nodeId) });

        items.Should().NotContain(i => i.Label == "Open Blueprint");
    }

    [Fact]
    public void GetItemsFor_omits_OpenBlueprint_item_for_regular_decorator_only_usage()
    {
        // Regression: the pre-existing "Add Decorator"-only construction path (no asset/catalog)
        // must keep returning exactly the one "Add Decorator" item.
        var asset = MakeAsset();
        var (_, nodeId) = AddPlainSequenceNode(asset);

        var model    = new BTreeGraphModel(asset);
        var sink     = new BTreeCommandSink(asset, model);
        var provider = new BTreeNodeContextMenuProvider(sink, model);

        var items = provider.GetItemsFor(new NodeId(nodeId), new[] { new NodeId(nodeId) });

        items.Should().ContainSingle();
        items[0].Label.Should().Be("Add Decorator");
    }
}
