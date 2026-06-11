using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class AssetBrowserPanelTests
{
    // ── Fakes ────────────────────────────────────────────────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Asset";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "";
        public bool IsDirty { get; init; }
        public bool IsEditorOwned { get; init; }
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    /// <summary>
    /// Recording fake catalog: tracks whether any mutation/side-effect method was called.
    /// The panel must never call these.
    /// </summary>
    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets;

        public FakeCatalog(params IEditableAsset[] assets)
        {
            _assets = new List<IEditableAsset>(assets);
        }

        public FakeCatalog(IEnumerable<IEditableAsset> assets)
        {
            _assets = new List<IEditableAsset>(assets);
        }

        public IReadOnlyList<IEditableAsset> All => _assets.AsReadOnly();
        public IEditableAsset? FindByAssetId(Guid assetId) => _assets.FirstOrDefault(a => a.AssetId == assetId);
        public IEditableAsset? FindByName(string name) => _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();

        public bool LoadCalled { get; private set; }
        public bool OpenDocumentCalled { get; private set; }

        public void RecordLoad() => LoadCalled = true;
        public void RecordOpenDocument() => OpenDocumentCalled = true;

#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67

        public void RaiseChanged() => Changed?.Invoke();
    }

    private sealed class FakeIconProvider : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle)
        {
            // Return a dummy handle — content doesn't matter for logic tests.
            handle = new IconHandle(1, 16, 16);
            return true;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="FolderTreeNode"/> by walking the tree path separated by '/'.
    /// </summary>
    private static FolderTreeNode? FindNode(FolderTreeNode root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return root;

        var segments = path.Split('/');
        FolderTreeNode? current = root;
        foreach (var segment in segments)
        {
            current = current?.Children.FirstOrDefault(c => c.Name == segment);
            if (current == null) return null;
        }
        return current;
    }

    private static AssetBrowserPanel CreatePanel(
        FakeCatalog catalog,
        AssetKindFilter kinds = AssetKindFilter.All)
    {
        var icons = new FakeIconProvider();
        var options = new AssetBrowserPanelOptions { Kinds = kinds };
        return new AssetBrowserPanel(catalog, icons, options);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Tabs_ReflectKindFilter
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// With Kinds = Blueprint | Hsm, Tabs contains exactly those two kinds,
    /// not BTree / Blackboard / Utility.
    /// </summary>
    [Fact]
    public void Tabs_ReflectKindFilter()
    {
        var catalog = new FakeCatalog();
        var panel = CreatePanel(catalog, kinds: AssetKindFilter.Blueprint | AssetKindFilter.Hsm);

        var tabs = panel.Tabs;

        Assert.Equal(2, tabs.Count);
        Assert.Contains(AssetKind.Blueprint, tabs);
        Assert.Contains(AssetKind.Hsm, tabs);
        Assert.DoesNotContain(AssetKind.BTree, tabs);
        Assert.DoesNotContain(AssetKind.Blackboard, tabs);
        Assert.DoesNotContain(AssetKind.Utility, tabs);
    }

    // ═════════════════════════════════════════════════════════════════
    //  PerKindTree_GroupsAssetsByRelPath
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Assets with SourceFilePath under the kind's Assets root produce a tree
    /// with correct folder hierarchy and leaf→asset mapping.
    /// </summary>
    [Fact]
    public void PerKindTree_GroupsAssetsByRelPath()
    {
        // Construct SourceFilePaths under the actual Assets/Blueprints base folder.
        var baseFolder = AssetRoots.AssetsFor(AssetKind.Blueprint);
        var guardPath = Path.Combine(baseFolder, "combat", "Guard.bp.json");
        var patrolPath = Path.Combine(baseFolder, "Patrol.bp.json");

        var guardAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Guard",
            Kind = AssetKind.Blueprint,
            SourceFilePath = guardPath
        };
        var patrolAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Patrol",
            Kind = AssetKind.Blueprint,
            SourceFilePath = patrolPath
        };
        // An asset of a different kind — must NOT appear in the Blueprint tree.
        var btreeAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "SomeTree",
            Kind = AssetKind.BTree,
            SourceFilePath = Path.Combine(AssetRoots.AssetsFor(AssetKind.BTree), "SomeTree.json")
        };

        var catalog = new FakeCatalog(guardAsset, patrolAsset, btreeAsset);
        var panel = CreatePanel(catalog, kinds: AssetKindFilter.Blueprint | AssetKindFilter.BTree);

        var blueprintTree = panel.TreeFor(AssetKind.Blueprint);

        // Root should have 2 children: folder "combat" (first — folders before leaves)
        // and leaf "Patrol.bp.json" (second).
        Assert.Equal(2, blueprintTree.Children.Count);

        // First child = folder "combat"
        var combatFolder = blueprintTree.Children[0];
        Assert.Equal("combat", combatFolder.Name);
        Assert.False(combatFolder.IsLeaf);
        Assert.Single(combatFolder.Children);

        // combat's child = leaf "Guard.bp.json"
        var guardLeaf = combatFolder.Children[0];
        Assert.Equal("Guard.bp.json", guardLeaf.Name);
        Assert.True(guardLeaf.IsLeaf);

        // Second child = leaf "Patrol.bp.json"
        var patrolLeaf = blueprintTree.Children[1];
        Assert.Equal("Patrol.bp.json", patrolLeaf.Name);
        Assert.True(patrolLeaf.IsLeaf);

        // ── Leaf → asset mapping ──────────────────────────────────
        var mappedGuard = panel.AssetForLeaf(AssetKind.Blueprint, guardLeaf);
        Assert.NotNull(mappedGuard);
        Assert.Equal(guardAsset.AssetId, mappedGuard!.AssetId);
        Assert.Equal("Guard", mappedGuard.Name);

        var mappedPatrol = panel.AssetForLeaf(AssetKind.Blueprint, patrolLeaf);
        Assert.NotNull(mappedPatrol);
        Assert.Equal(patrolAsset.AssetId, mappedPatrol!.AssetId);
        Assert.Equal("Patrol", mappedPatrol.Name);

        // Non-leaf node → null.
        Assert.Null(panel.AssetForLeaf(AssetKind.Blueprint, combatFolder));

        // The BTree asset must NOT appear in the Blueprint tree leaves.
        var treeLeaves = AllLeaves(blueprintTree);
        Assert.All(treeLeaves, leaf =>
        {
            var a = panel.AssetForLeaf(AssetKind.Blueprint, leaf);
            Assert.NotNull(a);
            Assert.NotEqual(btreeAsset.AssetId, a!.AssetId);
        });
    }

    // ═════════════════════════════════════════════════════════════════
    //  Row_CarriesKindIconKey
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Row_CarriesKindIconKey()
    {
        var bpAsset = new FakeAsset { Kind = AssetKind.Blueprint };
        var btreeAsset = new FakeAsset { Kind = AssetKind.BTree };
        var catalog = new FakeCatalog(bpAsset, btreeAsset);
        var panel = CreatePanel(catalog);

        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Blueprint), panel.RowIconKey(bpAsset));
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.BTree), panel.RowIconKey(btreeAsset));

        // Also verify a third kind for good measure.
        var hsmAsset = new FakeAsset { Kind = AssetKind.Hsm };
        Assert.Equal(AssetKindIcons.GetIconKey(AssetKind.Hsm), panel.RowIconKey(hsmAsset));
    }

    // ═════════════════════════════════════════════════════════════════
    //  DoubleClick_RaisesAssetActivated_WithAsset
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void DoubleClick_RaisesAssetActivated_WithAsset()
    {
        var asset = new FakeAsset { Name = "TestAsset", Kind = AssetKind.Blueprint };
        var catalog = new FakeCatalog(asset);
        var panel = CreatePanel(catalog);

        IEditableAsset? activatedAsset = null;
        panel.AssetActivated += a => activatedAsset = a;

        // Activate → event fires with that exact asset.
        panel.ActivateAsset(asset);
        Assert.NotNull(activatedAsset);
        Assert.Same(asset, activatedAsset);

        // SelectAsset → Selection is set.
        panel.SelectAsset(asset);
        Assert.Same(asset, panel.Selection);

        // Clear selection.
        panel.SelectAsset(null);
        Assert.Null(panel.Selection);

        // Neither activation nor selection performs any catalog side effect.
        Assert.False(catalog.LoadCalled,
            "Panel must not call Load/Open — it performs no side effects.");
        Assert.False(catalog.OpenDocumentCalled,
            "Panel must not call OpenDocument — it performs no side effects.");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static List<FolderTreeNode> AllLeaves(FolderTreeNode node)
    {
        var leaves = new List<FolderTreeNode>();
        CollectLeaves(node, leaves);
        return leaves;
    }

    private static void CollectLeaves(FolderTreeNode node, List<FolderTreeNode> leaves)
    {
        if (node.IsLeaf)
        {
            leaves.Add(node);
            return;
        }
        foreach (var child in node.Children)
        {
            CollectLeaves(child, leaves);
        }
    }
}
