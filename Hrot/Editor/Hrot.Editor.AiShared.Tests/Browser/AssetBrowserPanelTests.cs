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
        AssetKindFilter kinds = AssetKindFilter.All,
        AssetKind? initialKind = null,
        string? initialFullPath = null,
        IReadOnlyDictionary<AssetKind, string>? lastOpened = null,
        bool showAllTab = true)
    {
        var icons = new FakeIconProvider();
        var options = new AssetBrowserPanelOptions
        {
            Kinds = kinds,
            InitialKind = initialKind,
            InitialFullPath = initialFullPath,
            ShowAllTab = showAllTab
        };
        return new AssetBrowserPanel(catalog, icons, options, lastOpened);
    }

    // ═════════════════════════════════════════════════════════════════
    //  BATCH-11 tests (must still pass)
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

    /// <summary>
    /// RowIconKey returns the correct icon key string for each AssetKind.
    /// </summary>
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

    /// <summary>
    /// ActivateAsset raises AssetActivated; SelectAsset sets Selection.
    /// Neither performs catalog side effects.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════
    //  MTB-P4-T4: Filter + All tab + chips
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Setting Filter to a lowercase substring of a mixed-case asset name
    /// prunes FilteredTreeFor to matching leaves + ancestor folders, and
    /// FilteredFlatList to matching assets only.
    /// </summary>
    [Fact]
    public void Filter_Substring_CaseInsensitive_PrunesTreeAndList()
    {
        var baseFolder = AssetRoots.AssetsFor(AssetKind.Blueprint);

        // Mixed-case names: the filter "guard" (lowercase) should match "Guard".
        var guardAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Guard",
            Kind = AssetKind.Blueprint,
            SourceFilePath = Path.Combine(baseFolder, "combat", "Guard.bp.json")
        };
        var patrolAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "PATROL",        // uppercase name
            Kind = AssetKind.Blueprint,
            SourceFilePath = Path.Combine(baseFolder, "Patrol.bp.json")
        };
        var scoutAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Scout",
            Kind = AssetKind.Blueprint,
            SourceFilePath = Path.Combine(baseFolder, "Scout.bp.json")
        };

        var catalog = new FakeCatalog(guardAsset, patrolAsset, scoutAsset);
        var panel = CreatePanel(catalog, kinds: AssetKindFilter.Blueprint);

        // ── Before filter: tree has 3 items ────────────────────
        var fullTree = panel.TreeFor(AssetKind.Blueprint);
        Assert.Equal(3, AllLeaves(fullTree).Count);

        // ── Filter matching "Guard" (case-insensitive match on "guard") ──
        panel.Filter = "guard";

        // FilteredTreeFor: only Guard + its folder "combat" survive.
        var filteredTree = panel.FilteredTreeFor(AssetKind.Blueprint);
        var filteredLeaves = AllLeaves(filteredTree);
        Assert.Single(filteredLeaves);
        Assert.Equal("Guard.bp.json", filteredLeaves[0].Name);

        // The parent folder "combat" must be present.
        Assert.Single(filteredTree.Children);
        Assert.Equal("combat", filteredTree.Children[0].Name);
        Assert.False(filteredTree.Children[0].IsLeaf);

        // ── Filter matching "pat" (case-insensitive, matches "PATROL") ──
        panel.Filter = "pat";
        var filteredTree2 = panel.FilteredTreeFor(AssetKind.Blueprint);
        var filteredLeaves2 = AllLeaves(filteredTree2);
        Assert.Single(filteredLeaves2);
        Assert.Equal("Patrol.bp.json", filteredLeaves2[0].Name);

        // ── Filter matching nothing ──
        panel.Filter = "nonexistent";
        var filteredTree3 = panel.FilteredTreeFor(AssetKind.Blueprint);
        Assert.Empty(filteredTree3.Children);

        // ── FilteredFlatList also honors filter ──
        panel.Filter = "guard";
        var flatList = panel.FilteredFlatList();
        Assert.Single(flatList);
        Assert.Equal(guardAsset.AssetId, flatList[0].AssetId);

        // ── Clear filter restores full tree ──
        panel.Filter = "";
        var restoredTree = panel.FilteredTreeFor(AssetKind.Blueprint);
        Assert.Equal(3, AllLeaves(restoredTree).Count);
    }

    /// <summary>
    /// Disabling a kind chip removes assets of that kind from
    /// FilteredFlatList; re-enabling restores them. Per-kind tabs
    /// are unaffected by chip state.
    /// </summary>
    [Fact]
    public void AllTab_Chips_ToggleKindVisibility()
    {
        var baseFolder = AssetRoots.AssetsFor(AssetKind.Blueprint);
        var bpAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Guard",
            Kind = AssetKind.Blueprint,
            SourceFilePath = Path.Combine(baseFolder, "Guard.bp.json")
        };

        var btreeFolder = AssetRoots.AssetsFor(AssetKind.BTree);
        var btreeAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "CombatTree",
            Kind = AssetKind.BTree,
            SourceFilePath = Path.Combine(btreeFolder, "CombatTree.json")
        };

        var catalog = new FakeCatalog(bpAsset, btreeAsset);
        var panel = CreatePanel(catalog,
            kinds: AssetKindFilter.Blueprint | AssetKindFilter.BTree);

        // ── Initially all chips enabled → both assets in flat list ──
        Assert.True(panel.IsKindChipEnabled(AssetKind.Blueprint));
        Assert.True(panel.IsKindChipEnabled(AssetKind.BTree));

        var fullList = panel.FilteredFlatList();
        Assert.Equal(2, fullList.Count);
        Assert.Contains(fullList, a => a.Kind == AssetKind.Blueprint);
        Assert.Contains(fullList, a => a.Kind == AssetKind.BTree);

        // ── Disable Blueprint chip ──
        panel.SetKindChip(AssetKind.Blueprint, false);
        Assert.False(panel.IsKindChipEnabled(AssetKind.Blueprint));
        Assert.True(panel.IsKindChipEnabled(AssetKind.BTree));

        var filteredList = panel.FilteredFlatList();
        Assert.Single(filteredList);
        Assert.Equal(AssetKind.BTree, filteredList[0].Kind);

        // ── Re-enable Blueprint chip ──
        panel.SetKindChip(AssetKind.Blueprint, true);

        var restoredList = panel.FilteredFlatList();
        Assert.Equal(2, restoredList.Count);
        Assert.Contains(restoredList, a => a.Kind == AssetKind.Blueprint);

        // ── Toggle via ToggleKindChip ──
        panel.ToggleKindChip(AssetKind.BTree);
        Assert.False(panel.IsKindChipEnabled(AssetKind.BTree));
        var afterToggle = panel.FilteredFlatList();
        Assert.Single(afterToggle);
        Assert.Equal(AssetKind.Blueprint, afterToggle[0].Kind);

        // ── Blueprint tree is unaffected by chip state ──
        panel.Filter = "";
        var bpTree = panel.TreeFor(AssetKind.Blueprint);
        Assert.Single(AllLeaves(bpTree));
    }

    /// <summary>
    /// FilteredFlatList returns a flat list spanning multiple kinds
    /// with no tree structure. The All tab concept has no associated tree.
    /// </summary>
    [Fact]
    public void AllTab_NoTree_FlatListOnly()
    {
        var baseFolder = AssetRoots.AssetsFor(AssetKind.Blueprint);
        var bpAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Guard",
            Kind = AssetKind.Blueprint,
            SourceFilePath = Path.Combine(baseFolder, "combat", "Guard.bp.json")
        };

        var btreeFolder = AssetRoots.AssetsFor(AssetKind.BTree);
        var btreeAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "CombatTree",
            Kind = AssetKind.BTree,
            SourceFilePath = Path.Combine(btreeFolder, "CombatTree.json")
        };

        var hsmFolder = AssetRoots.AssetsFor(AssetKind.Hsm);
        var hsmAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "IdleHsm",
            Kind = AssetKind.Hsm,
            SourceFilePath = Path.Combine(hsmFolder, "IdleHsm.json")
        };

        var catalog = new FakeCatalog(bpAsset, btreeAsset, hsmAsset);
        var panel = CreatePanel(catalog,
            kinds: AssetKindFilter.Blueprint | AssetKindFilter.BTree | AssetKindFilter.Hsm);

        // ── FilteredFlatList returns assets across multiple kinds ──
        var flatList = panel.FilteredFlatList();
        Assert.Equal(3, flatList.Count);

        // Verify all three kinds are represented.
        var kindsInList = flatList.Select(a => a.Kind).Distinct().ToList();
        Assert.Equal(3, kindsInList.Count);
        Assert.Contains(AssetKind.Blueprint, kindsInList);
        Assert.Contains(AssetKind.BTree, kindsInList);
        Assert.Contains(AssetKind.Hsm, kindsInList);

        // ── Sorted by kind, then name ──
        for (int i = 1; i < flatList.Count; i++)
        {
            var prev = flatList[i - 1];
            var curr = flatList[i];
            Assert.True(
                prev.Kind < curr.Kind
                || (prev.Kind == curr.Kind
                    && StringComparer.OrdinalIgnoreCase.Compare(prev.Name, curr.Name) <= 0),
                $"Flat list not sorted at index {i}: {prev.Kind}/{prev.Name} → {curr.Kind}/{curr.Name}");
        }

        // ── Flat list is NOT a tree — each entry is a direct IEditableAsset ──
        // The model exposes no tree for the "All" concept itself.
        // FilteredFlatList is a flat IReadOnlyList<IEditableAsset>.
        Assert.IsAssignableFrom<IReadOnlyList<IEditableAsset>>(flatList);
    }

    // ═════════════════════════════════════════════════════════════════
    //  MTB-P4-T5: Initial reveal + last-opened memory
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// With InitialKind=Blueprint and InitialFullPath="combat/patrol/Guard.bp.json",
    /// ExpandedFolders(Blueprint) contains the ancestor folder paths "combat" and
    /// "combat/patrol", and Selection is the Guard asset.
    /// </summary>
    [Fact]
    public void InitialFullPath_ExpandsAncestors_AndSelectsLeaf()
    {
        var baseFolder = AssetRoots.AssetsFor(AssetKind.Blueprint);
        var guardPath = Path.Combine(baseFolder, "combat", "patrol", "Guard.bp.json");
        var guardAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Guard",
            Kind = AssetKind.Blueprint,
            SourceFilePath = guardPath
        };
        // Sibling asset — should NOT be selected.
        var scoutAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Scout",
            Kind = AssetKind.Blueprint,
            SourceFilePath = Path.Combine(baseFolder, "combat", "Scout.bp.json")
        };

        var catalog = new FakeCatalog(guardAsset, scoutAsset);
        var panel = CreatePanel(catalog,
            kinds: AssetKindFilter.Blueprint,
            initialKind: AssetKind.Blueprint,
            initialFullPath: "combat/patrol/Guard.bp.json");

        // ── ExpandedFolders contains ancestor paths ──
        var expanded = panel.ExpandedFolders(AssetKind.Blueprint);
        Assert.NotNull(expanded);
        var expandedList = expanded.ToList();
        Assert.Contains("combat", expandedList);
        Assert.Contains("combat/patrol", expandedList);
        // Must NOT contain the leaf itself (only ancestor folders).
        Assert.DoesNotContain("combat/patrol/Guard.bp.json", expandedList);

        // ── Selection is the Guard asset ──
        Assert.NotNull(panel.Selection);
        Assert.Equal(guardAsset.AssetId, panel.Selection!.AssetId);
        Assert.Equal("Guard", panel.Selection.Name);

        // ── Scout is NOT selected ──
        Assert.NotEqual(scoutAsset.AssetId, panel.Selection.AssetId);
    }

    /// <summary>
    /// ActivateAsset updates LastOpenedByKind[kind] to the asset's relpath.
    /// Constructing a new panel and restoring that map pre-selects/reveals
    /// the remembered relpath. The map is per-kind: activating a Blueprint
    /// does not change the BTree entry.
    /// </summary>
    [Fact]
    public void LastOpened_PersistsAndRestores_PerKind()
    {
        var baseFolder = AssetRoots.AssetsFor(AssetKind.Blueprint);
        var btreeFolder = AssetRoots.AssetsFor(AssetKind.BTree);

        var bpAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "Guard",
            Kind = AssetKind.Blueprint,
            SourceFilePath = Path.Combine(baseFolder, "combat", "Guard.bp.json")
        };
        var btreeAsset = new FakeAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "CombatTree",
            Kind = AssetKind.BTree,
            SourceFilePath = Path.Combine(btreeFolder, "tactical", "CombatTree.json")
        };

        var catalog1 = new FakeCatalog(bpAsset, btreeAsset);
        var panel1 = CreatePanel(catalog1,
            kinds: AssetKindFilter.Blueprint | AssetKindFilter.BTree);

        // ── Initially, no last-opened entries ──
        var initialMap = panel1.LastOpenedByKind;
        Assert.Empty(initialMap);

        // ── Activate Blueprint → updates LastOpenedByKind ──
        panel1.ActivateAsset(bpAsset);
        var mapAfterBp = panel1.LastOpenedByKind;
        Assert.True(mapAfterBp.ContainsKey(AssetKind.Blueprint));
        Assert.Equal("combat/Guard.bp.json", mapAfterBp[AssetKind.Blueprint]);

        // ── BTree entry NOT affected by Blueprint activation ──
        Assert.False(mapAfterBp.ContainsKey(AssetKind.BTree),
            "Activating a Blueprint must not change the BTree last-opened entry.");

        // ── Activate BTree → updates BTree entry; Blueprint entry preserved ──
        panel1.ActivateAsset(btreeAsset);
        var mapAfterBtree = panel1.LastOpenedByKind;
        Assert.Equal("combat/Guard.bp.json", mapAfterBtree[AssetKind.Blueprint]);
        Assert.Equal("tactical/CombatTree.json", mapAfterBtree[AssetKind.BTree]);

        // ── Construct new panel and restore the map → pre-selects Blueprint ──
        var catalog2 = new FakeCatalog(bpAsset, btreeAsset);
        var panel2 = CreatePanel(catalog2,
            kinds: AssetKindFilter.Blueprint | AssetKindFilter.BTree,
            initialKind: AssetKind.Blueprint,
            lastOpened: mapAfterBtree);

        // Selection should reflect the remembered Blueprint asset.
        Assert.NotNull(panel2.Selection);
        Assert.Equal(bpAsset.AssetId, panel2.Selection!.AssetId);

        // Expanded folders should contain the ancestor path of the Blueprint asset.
        var expanded = panel2.ExpandedFolders(AssetKind.Blueprint);
        Assert.Contains("combat", expanded);

        // ── RestoreLastOpened method also works ──
        var catalog3 = new FakeCatalog(bpAsset, btreeAsset);
        var panel3 = CreatePanel(catalog3,
            kinds: AssetKindFilter.Blueprint | AssetKindFilter.BTree,
            lastOpened: null); // no initial map

        // Before restore: no selection (no InitialFullPath, no last-opened).
        Assert.Null(panel3.Selection);

        // Restoring should populate LastOpenedByKind but doesn't re-trigger reveal
        // (reveal only happens in constructor). So we verify the map is stored.
        panel3.RestoreLastOpened(mapAfterBtree);
        var restoredMap = panel3.LastOpenedByKind;
        Assert.Equal("combat/Guard.bp.json", restoredMap[AssetKind.Blueprint]);
        Assert.Equal("tactical/CombatTree.json", restoredMap[AssetKind.BTree]);
    }

    // ═════════════════════════════════════════════════════════════════
    //  GetAncestorPaths
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void GetAncestorPaths_ReturnsCorrectAncestors()
    {
        // Single segment → empty.
        var result1 = AssetBrowserPanel.GetAncestorPaths("Guard.bp.json");
        Assert.Empty(result1);

        // Two segments.
        var result2 = AssetBrowserPanel.GetAncestorPaths("combat/Guard.bp.json");
        Assert.Single(result2);
        Assert.Equal("combat", result2[0]);

        // Three segments.
        var result3 = AssetBrowserPanel.GetAncestorPaths("combat/patrol/Guard.bp.json");
        Assert.Equal(2, result3.Count);
        Assert.Equal("combat", result3[0]);
        Assert.Equal("combat/patrol", result3[1]);

        // Empty / null.
        Assert.Empty(AssetBrowserPanel.GetAncestorPaths(""));
        Assert.Empty(AssetBrowserPanel.GetAncestorPaths(null!));
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
