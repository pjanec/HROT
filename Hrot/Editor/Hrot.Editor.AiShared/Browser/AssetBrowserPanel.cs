using ImGuiNET;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Bitmask filter for <see cref="AssetKind"/> values used by
/// <see cref="AssetBrowserPanelOptions"/> to control which tabs appear.
/// </summary>
[Flags]
public enum AssetKindFilter
{
    None        = 0,
    Scenario    = 1,
    Blueprint   = 2,
    BTree       = 4,
    Hsm         = 8,
    Blackboard  = 16,
    Utility     = 32,
    All         = ~0
}

/// <summary>
/// Mapping helpers between <see cref="AssetKindFilter"/> and <see cref="AssetKind"/>.
/// </summary>
public static class AssetKindFilterMapping
{
    /// <summary>
    /// Returns the <see cref="AssetKindFilter"/> flag corresponding to
    /// <paramref name="kind"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for unknown <see cref="AssetKind"/> values.
    /// </exception>
    public static AssetKindFilter FromKind(AssetKind kind) => kind switch
    {
        AssetKind.Blueprint  => AssetKindFilter.Blueprint,
        AssetKind.BTree      => AssetKindFilter.BTree,
        AssetKind.Hsm        => AssetKindFilter.Hsm,
        AssetKind.Blackboard => AssetKindFilter.Blackboard,
        AssetKind.Utility    => AssetKindFilter.Utility,
        AssetKind.Scenario   => AssetKindFilter.Scenario,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
            $"Unknown {nameof(AssetKind)} value: {kind}")
    };

    /// <summary>
    /// Returns the permitted <see cref="AssetKind"/> values from <paramref name="filter"/>,
    /// in enum declaration order.
    /// </summary>
    public static IReadOnlyList<AssetKind> PermittedKinds(AssetKindFilter filter)
    {
        var kinds = new List<AssetKind>(6);
        if (filter.HasFlag(AssetKindFilter.Blueprint))  kinds.Add(AssetKind.Blueprint);
        if (filter.HasFlag(AssetKindFilter.BTree))      kinds.Add(AssetKind.BTree);
        if (filter.HasFlag(AssetKindFilter.Hsm))        kinds.Add(AssetKind.Hsm);
        if (filter.HasFlag(AssetKindFilter.Blackboard)) kinds.Add(AssetKind.Blackboard);
        if (filter.HasFlag(AssetKindFilter.Utility))    kinds.Add(AssetKind.Utility);
        if (filter.HasFlag(AssetKindFilter.Scenario))   kinds.Add(AssetKind.Scenario);
        return kinds.AsReadOnly();
    }
}

/// <summary>
/// Options for <see cref="AssetBrowserPanel"/>. Define all fields now so later
/// batches do not change the shape; only <see cref="Kinds"/> is wired in this batch.
/// </summary>
public sealed class AssetBrowserPanelOptions
{
    /// <summary>
    /// Which asset kinds are permitted. Defaults to <see cref="AssetKindFilter.All"/>.
    /// <b>Wired in this batch (MTB-P4-T3).</b>
    /// </summary>
    public AssetKindFilter Kinds { get; init; } = AssetKindFilter.All;

    /// <summary>
    /// When <see langword="true"/>, an "All" tab with a flat list and kind-filter
    /// chips is shown. <b>Wired in MTB-P4-T4.</b>
    /// </summary>
    public bool ShowAllTab { get; init; } = true;

    /// <summary>
    /// The kind to activate on first draw. <b>Wired in MTB-P4-T5.</b>
    /// </summary>
    public AssetKind? InitialKind { get; init; }

    /// <summary>
    /// The relative-to-root path to auto-expand and select on first draw.
    /// <b>Wired in MTB-P4-T5.</b>
    /// </summary>
    public string? InitialFullPath { get; init; }
}

/// <summary>
/// A generic, reusable Asset Browser content panel (§10.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Design:</b> logic is separated from ImGui draw. The testable model exposes
/// <see cref="Tabs"/>, <see cref="TreeFor"/>, <see cref="AssetForLeaf"/>,
/// <see cref="RowIconKey"/>, <see cref="Selection"/>, <see cref="SelectAsset"/>,
/// <see cref="ActivateAsset"/>, and <see cref="AssetActivated"/>.
/// <see cref="DrawContent"/> renders the model via ImGui but performs no logic
/// beyond calling the model methods.
/// </para>
/// <para>
/// The panel performs <b>no side effects</b> — it never opens documents or loads
/// scenarios. The host decides what to do with <see cref="AssetActivated"/>.
/// </para>
/// </remarks>
public sealed class AssetBrowserPanel
{
    private readonly IAssetCatalog _catalog;
    private readonly IIconProvider _icons;
    private readonly AssetBrowserPanelOptions _options;
    private int _activeTabIndex;
    private string _filter = "";

    // Per-kind cache rebuilt on catalog Changed.
    private readonly Dictionary<AssetKind, FolderTreeNode> _trees = new();
    private readonly Dictionary<AssetKind, Dictionary<string, IEditableAsset>> _leafMap = new();

    // Kind chips — only used for the "All" tab.
    private readonly Dictionary<AssetKind, bool> _kindChipsEnabled = new();

    // Last-opened-per-kind memory (§10.1).
    private readonly Dictionary<AssetKind, string> _lastOpenedByKind = new();

    // Expanded folder paths per kind (initial reveal from InitialFullPath / last-opened).
    private readonly Dictionary<AssetKind, HashSet<string>> _expandedFolders = new();

    /// <summary>
    /// The permitted kinds derived from <see cref="AssetBrowserPanelOptions.Kinds"/>.
    /// Always all permitted kinds (filter-driven, not data-driven — a kind with zero
    /// assets still appears as a tab with an empty tree).
    /// </summary>
    public IReadOnlyList<AssetKind> Tabs { get; }

    /// <summary>
    /// The currently selected asset, or <see langword="null"/>.
    /// Set via <see cref="SelectAsset"/>.
    /// </summary>
    public IEditableAsset? Selection { get; private set; }

    /// <summary>
    /// Raised when an asset is activated (double-click / Enter).
    /// The panel performs no side effects — the host handles the event.
    /// </summary>
    public event Action<IEditableAsset>? AssetActivated;

    // ── T4: Filter & chips ────────────────────────────────────────────

    /// <summary>
    /// Incremental case-insensitive name filter applied in every tab (§10.1).
    /// Set to <see cref="string.Empty"/> or <see langword="null"/> to clear.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set => _filter = value ?? "";
    }

    /// <summary>
    /// Returns whether the kind chip for <paramref name="kind"/> is currently
    /// enabled (affects <see cref="FilteredFlatList"/>). Only meaningful for
    /// the "All" tab; per-kind tabs ignore chip state.
    /// </summary>
    public bool IsKindChipEnabled(AssetKind kind)
    {
        return _kindChipsEnabled.TryGetValue(kind, out var v) && v;
    }

    /// <summary>
    /// Sets the enabled state of the kind chip for <paramref name="kind"/>.
    /// </summary>
    public void SetKindChip(AssetKind kind, bool enabled)
    {
        _kindChipsEnabled[kind] = enabled;
    }

    /// <summary>
    /// Toggles the kind chip for <paramref name="kind"/>.
    /// </summary>
    public void ToggleKindChip(AssetKind kind)
    {
        SetKindChip(kind, !IsKindChipEnabled(kind));
    }

    // ── T5: Last-opened memory & initial reveal ───────────────────────

    /// <summary>
    /// A per-kind map of the last activated asset's relative path.
    /// The host persists this map across sessions (§10.1).
    /// </summary>
    public IReadOnlyDictionary<AssetKind, string> LastOpenedByKind =>
        new Dictionary<AssetKind, string>(_lastOpenedByKind);

    /// <summary>
    /// Restores last-opened-per-kind memory (e.g. from editor session prefs).
    /// Entries for kinds not in <see cref="Tabs"/> are silently ignored.
    /// Does not trigger a re-reveal — call this before construction or
    /// supply the map via the constructor.
    /// </summary>
    public void RestoreLastOpened(IReadOnlyDictionary<AssetKind, string>? map)
    {
        if (map == null) return;
        foreach (var (kind, relPath) in map)
        {
            if (Tabs.Contains(kind))
                _lastOpenedByKind[kind] = relPath;
        }
    }

    /// <summary>
    /// Returns the set of folder <see cref="FolderTreeNode.FullPath"/>s that
    /// should be expanded for <paramref name="kind"/>. Populated by
    /// <see cref="AssetBrowserPanelOptions.InitialFullPath"/> or
    /// <see cref="LastOpenedByKind"/> on construction.
    /// </summary>
    public IReadOnlyCollection<string> ExpandedFolders(AssetKind kind)
    {
        return _expandedFolders.TryGetValue(kind, out var set)
            ? set.ToArray()
            : Array.Empty<string>();
    }

    // ── Construction ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="AssetBrowserPanel"/>.
    /// </summary>
    /// <param name="catalog">The asset catalog (never <see langword="null"/>).</param>
    /// <param name="icons">The icon provider for resolving kind-icon keys (never <see langword="null"/>).</param>
    /// <param name="options">Panel options (never <see langword="null"/>).</param>
    /// <param name="lastOpened">
    /// Optional per-kind last-opened map to restore (e.g. from editor session prefs).
    /// Applied after <see cref="AssetBrowserPanelOptions.InitialFullPath"/> takes
    /// precedence for the initial kind.
    /// </param>
    public AssetBrowserPanel(
        IAssetCatalog catalog,
        IIconProvider icons,
        AssetBrowserPanelOptions options,
        IReadOnlyDictionary<AssetKind, string>? lastOpened = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        Tabs = AssetKindFilterMapping.PermittedKinds(options.Kinds);

        // Initialise kind chips — all-on among permitted kinds.
        foreach (var kind in Tabs)
            _kindChipsEnabled[kind] = true;

        // Restore last-opened memory (before rebuild so leaf maps exist).
        RestoreLastOpened(lastOpened);

        RebuildTrees();
        _catalog.Changed += OnCatalogChanged;

        // Apply initial reveal (InitialFullPath / last-opened → Selection + expanded folders).
        ApplyInitialReveal();
    }

    // ── Tree / leaf model (BATCH-11) ──────────────────────────────────

    /// <summary>
    /// Returns the <b>unfiltered</b> folder tree for <paramref name="kind"/>, built from
    /// the relative paths of all assets of that kind in the catalog.
    /// If the kind is not a permitted tab, returns an empty tree.
    /// </summary>
    public FolderTreeNode TreeFor(AssetKind kind)
    {
        return _trees.TryGetValue(kind, out var tree)
            ? tree
            : FolderTreePicker.Build(null);
    }

    /// <summary>
    /// Returns the folder tree for <paramref name="kind"/>, pruned by the
    /// current <see cref="Filter"/>: only leaves whose asset <c>Name</c> matches
    /// the filter (case-insensitive substring) and their ancestor folders are
    /// kept. When <see cref="Filter"/> is empty, returns the same tree as
    /// <see cref="TreeFor"/>.
    /// </summary>
    public FolderTreeNode FilteredTreeFor(AssetKind kind)
    {
        var tree = TreeFor(kind);
        if (string.IsNullOrEmpty(_filter))
            return tree;
        return PruneTree(tree, kind) ?? FolderTreePicker.Build(null);
    }

    /// <summary>
    /// Returns the <see cref="IEditableAsset"/> represented by a leaf node
    /// in the tree for <paramref name="kind"/>, or <see langword="null"/>
    /// if the node is not a leaf or is not found.
    /// </summary>
    public IEditableAsset? AssetForLeaf(AssetKind kind, FolderTreeNode leaf)
    {
        if (leaf == null) throw new ArgumentNullException(nameof(leaf));
        if (!leaf.IsLeaf) return null;

        return _leafMap.TryGetValue(kind, out var map)
               && map.TryGetValue(leaf.FullPath, out var asset)
            ? asset
            : null;
    }

    /// <summary>
    /// Returns a flat list of all assets across all permitted kinds whose
    /// kind chip is enabled and whose <c>Name</c> matches the current
    /// <see cref="Filter"/> (case-insensitive substring). Only meaningful
    /// for the "All" tab; per-kind tabs use <see cref="FilteredTreeFor"/>.
    /// </summary>
    public IReadOnlyList<IEditableAsset> FilteredFlatList()
    {
        var result = new List<IEditableAsset>();
        foreach (var kind in Tabs)
        {
            if (!IsKindChipEnabled(kind))
                continue;
            if (!_leafMap.TryGetValue(kind, out var map))
                continue;
            foreach (var (_, asset) in map)
            {
                if (string.IsNullOrEmpty(_filter)
                    || asset.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(asset);
                }
            }
        }

        // Deterministic sort: by kind, then by name (ordinal ignore-case).
        result.Sort((a, b) =>
        {
            var kindCmp = a.Kind.CompareTo(b.Kind);
            if (kindCmp != 0)
                return kindCmp;
            return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        return result.AsReadOnly();
    }

    /// <summary>
    /// Returns the <see cref="IIconProvider"/> key for the given asset's row icon.
    /// Punch-list #9: an asset may override its kind default via <see cref="IAssetIconKeyProvider"/>
    /// (e.g. a Blueprint distinguishing Action / Condition / Function); otherwise the per-kind icon
    /// from <see cref="AssetKindIcons.GetIconKey"/> is used.
    /// </summary>
    public string RowIconKey(IEditableAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        return AssetKindIcons.ResolveIconKey(asset);
    }

    /// <summary>
    /// Sets <see cref="Selection"/> to <paramref name="asset"/> (single-click highlight).
    /// Pass <see langword="null"/> to clear selection.
    /// </summary>
    public void SelectAsset(IEditableAsset? asset)
    {
        Selection = asset;
    }

    // ── BATCH-26: Programmatic tab switching ──────────────────────────

    /// <summary>
    /// The index of the <see cref="Tabs"/> entry to activate on the next
    /// <see cref="DrawContent"/> call, or <see langword="null"/> for no override.
    /// Consumed once and cleared after the tab bar is drawn.
    /// </summary>
    internal int? RequestedTabIndex => _requestedTabIndex;

    private int? _requestedTabIndex;

    /// <summary>
    /// Cycles to the next permitted-kind tab (wraps).  When the "All" tab is
    /// visible, the cycle goes All → first kind → second kind → … → All.
    /// Does nothing when no tabs are present and <see cref="AssetBrowserPanelOptions.ShowAllTab"/>
    /// is false.
    /// </summary>
    public void SelectNextTab()
    {
        int count = TabCount;
        if (count <= 1) return;

        int current = CurrentTabLogicalIndex;
        int next = current + 1;
        if (next >= count) next = 0;
        _requestedTabIndex = next;
    }

    /// <summary>
    /// Cycles to the previous permitted-kind tab (wraps).  Mirrors
    /// <see cref="SelectNextTab"/> in reverse.
    /// </summary>
    public void SelectPreviousTab()
    {
        int count = TabCount;
        if (count <= 1) return;

        int current = CurrentTabLogicalIndex;
        int prev = current - 1;
        if (prev < 0) prev = count - 1;
        _requestedTabIndex = prev;
    }

    /// <summary>
    /// The total number of logical tabs (1 for "All" if shown, plus
    /// <see cref="Tabs"/> count).
    /// </summary>
    internal int TabCount
    {
        get
        {
            int count = _options.ShowAllTab ? 1 : 0;
            count += Tabs.Count;
            return count;
        }
    }

    /// <summary>
    /// The currently active logical tab index: 0 = "All" (if shown),
    /// 1..N = <see cref="Tabs"/>[index-1]. Uses <see cref="_activeTabIndex"/>
    /// which tracks the last-clicked per-kind tab.
    /// </summary>
    private int CurrentTabLogicalIndex
    {
        get
        {
            // If "All" tab is visible and was last active (no explicit kind-tab
            // click tracked beyond _activeTabIndex), approximate: if _activeTabIndex
            // hasn't been set by clicking a kind tab, default to All (0).
            // The active tab is the one last clicked; we track this in DrawContent
            // via _lastDrawnTabLogicalIndex.
            return _lastDrawnTabLogicalIndex;
        }
    }

    /// <summary>
    /// Tracks the last logical tab index that was drawn (set by DrawContent).
    /// Used by <see cref="CurrentTabLogicalIndex"/>.
    /// </summary>
    private int _lastDrawnTabLogicalIndex;

    /// <summary>
    /// Raises the <see cref="AssetActivated"/> event with <paramref name="asset"/>
    /// (double-click / Enter) and updates <see cref="LastOpenedByKind"/> for the
    /// asset's kind. The panel performs no side effects.
    /// </summary>
    public void ActivateAsset(IEditableAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        // Update last-opened-per-kind memory.
        var baseFolder = BaseFolderFor(asset.Kind);
        var relPath = AssetRelPath.RelPath(asset, baseFolder);
        _lastOpenedByKind[asset.Kind] = relPath;

        AssetActivated?.Invoke(asset);
    }

    // ── Draw ───────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the panel content via ImGui: per-kind tabs, a folder tree for
    /// the active tab, and rows with kind icons. When <see cref="AssetBrowserPanelOptions.ShowAllTab"/>
    /// is <see langword="true"/>, an "All" tab with kind chips and a flat list
    /// is rendered first.
    /// </summary>
    /// <remarks>
    /// BATCH-26: when <see cref="_requestedTabIndex"/> is set (via
    /// <see cref="SelectNextTab"/> / <see cref="SelectPreviousTab"/>), the
    /// matching tab gets <see cref="ImGuiTabItemFlags.SetSelected"/> for one
    /// frame, then the request is cleared.
    /// </remarks>
    public void DrawContent()
    {
        if (Tabs.Count == 0 && !_options.ShowAllTab)
            return;

        int allTabOffset = _options.ShowAllTab ? 1 : 0;
        bool hasRequested = _requestedTabIndex.HasValue;
        int requested = _requestedTabIndex ?? -1;

        // ── Tab bar ─────────────────────────────────────────────────
        if (ImGui.BeginTabBar("##AssetBrowserTabs"))
        {
            // "All" tab (when enabled).
            if (_options.ShowAllTab)
            {
                bool allTabOpen = true;
                ImGuiTabItemFlags allFlags = ImGuiTabItemFlags.None;
                if (hasRequested && requested == 0)
                    allFlags |= ImGuiTabItemFlags.SetSelected;

                if (ImGui.BeginTabItem("All", ref allTabOpen, allFlags))
                {
                    _lastDrawnTabLogicalIndex = 0;
                    DrawAllTab();
                    ImGui.EndTabItem();
                }
            }

            for (int i = 0; i < Tabs.Count; i++)
            {
                var kind = Tabs[i];
                var label = kind.ToString();
                bool tabOpen = true;

                int logicalIndex = i + allTabOffset;
                ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
                if (hasRequested && requested == logicalIndex)
                    flags |= ImGuiTabItemFlags.SetSelected;

                if (ImGui.BeginTabItem(label, ref tabOpen, flags))
                {
                    _activeTabIndex = i;
                    _lastDrawnTabLogicalIndex = logicalIndex;
                    DrawKindTab(kind);
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }

        // Clear the one-frame tab-switch request.
        _requestedTabIndex = null;
    }

    private void DrawFilterBox()
    {
        var filter = _filter;
        ImGui.InputText("Filter", ref filter, 256);
        if (filter != _filter)
            Filter = filter;
    }

    private void DrawAllTab()
    {
        DrawFilterBox();

        // Kind chips — one checkbox per permitted kind.
        foreach (var kind in Tabs)
        {
            bool enabled = IsKindChipEnabled(kind);
            if (ImGui.Checkbox(kind.ToString(), ref enabled))
                SetKindChip(kind, enabled);
            ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.Separator();

        // Flat list.
        var assets = FilteredFlatList();
        if (assets.Count == 0)
        {
            ImGui.TextDisabled("No assets");
            return;
        }

        foreach (var asset in assets)
        {
            DrawFlatRow(asset);
        }
    }

    private void DrawKindTab(AssetKind kind)
    {
        DrawFilterBox();

        var tree = FilteredTreeFor(kind);
        if (tree.Children.Count == 0)
        {
            ImGui.TextDisabled("No assets");
            return;
        }

        var expandedSet = _expandedFolders.TryGetValue(kind, out var set) ? set : null;
        foreach (var child in tree.Children)
        {
            DrawTreeNode(kind, child, expandedSet);
        }
    }

    private void DrawTreeNode(AssetKind kind, FolderTreeNode node, HashSet<string>? expandedSet)
    {
        if (node.IsLeaf)
        {
            DrawLeafRow(kind, node);
            return;
        }

        // Folder node: DefaultOpen if in expanded set, or if expanded set is null (no
        // initial reveal — backward-compatible "open all" behavior).
        bool defaultOpen = expandedSet == null || expandedSet.Contains(node.FullPath);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow
                                   | ImGuiTreeNodeFlags.OpenOnDoubleClick;
        if (defaultOpen)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;

        bool isOpen = ImGui.TreeNodeEx(node.Name, flags);

        if (isOpen)
        {
            foreach (var child in node.Children)
            {
                DrawTreeNode(kind, child, expandedSet);
            }
            ImGui.TreePop();
        }
    }

    private void DrawLeafRow(AssetKind kind, FolderTreeNode leaf)
    {
        var asset = AssetForLeaf(kind, leaf);
        if (asset == null)
            return;

        var iconKey = RowIconKey(asset);
        var hasIcon = _icons.TryGet(iconKey, out var icon);

        // Selection highlight
        bool isSelected = ReferenceEquals(Selection, asset);
        if (isSelected)
        {
            var selectColor = ImGui.GetColorU32(ImGuiCol.Header);
            var cursorPos = ImGui.GetCursorScreenPos();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.GetWindowDrawList().AddRectFilled(
                cursorPos,
                cursorPos + new System.Numerics.Vector2(rowWidth, ImGui.GetFrameHeight()),
                selectColor);
        }

        // Icon (16x16 alongside text) — fall back to text if no icon.
        if (hasIcon)
        {
            ImGui.Text(" * " + leaf.Name);
        }
        else
        {
            ImGui.Text("   " + leaf.Name);
        }

        // Click handling
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectAsset(asset);
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            ActivateAsset(asset);
        }
    }

    private void DrawFlatRow(IEditableAsset asset)
    {
        var iconKey = RowIconKey(asset);
        var hasIcon = _icons.TryGet(iconKey, out var icon);

        // Selection highlight
        bool isSelected = ReferenceEquals(Selection, asset);
        if (isSelected)
        {
            var selectColor = ImGui.GetColorU32(ImGuiCol.Header);
            var cursorPos = ImGui.GetCursorScreenPos();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.GetWindowDrawList().AddRectFilled(
                cursorPos,
                cursorPos + new System.Numerics.Vector2(rowWidth, ImGui.GetFrameHeight()),
                selectColor);
        }

        if (hasIcon)
        {
            ImGui.Text(" * " + asset.Name);
        }
        else
        {
            ImGui.Text("   " + asset.Name);
        }

        // Click handling
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectAsset(asset);
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            ActivateAsset(asset);
        }
    }

    // ── Internals ──────────────────────────────────────────────────────

    private void OnCatalogChanged(AssetKind kind)
    {
        RebuildTrees();
    }

    private void RebuildTrees()
    {
        _trees.Clear();
        _leafMap.Clear();

        foreach (var kind in Tabs)
        {
            var kindAssets = _catalog.All.Where(a => a.Kind == kind).ToList();
            var baseFolder = BaseFolderFor(kind);
            var relPaths = new List<string>(kindAssets.Count);
            var map = new Dictionary<string, IEditableAsset>(kindAssets.Count);

            foreach (var asset in kindAssets)
            {
                var relPath = AssetRelPath.RelPath(asset, baseFolder);
                relPaths.Add(relPath);
                // In case of duplicate relpaths (shouldn't happen), last-writer wins.
                map[relPath] = asset;
            }

            _trees[kind] = FolderTreePicker.Build(relPaths);
            _leafMap[kind] = map;
        }
    }

    /// <summary>
    /// Applies the initial reveal logic: if <see cref="AssetBrowserPanelOptions.InitialFullPath"/>
    /// is set for <see cref="AssetBrowserPanelOptions.InitialKind"/> (or the first tab),
    /// expands the ancestor folders and selects the matching leaf. Falls back to
    /// <see cref="LastOpenedByKind"/> when no explicit initial path is provided.
    /// </summary>
    private void ApplyInitialReveal()
    {
        var targetKind = _options.InitialKind ?? Tabs.FirstOrDefault();
        if (!Tabs.Contains(targetKind))
            return;

        string? targetPath = _options.InitialFullPath;

        // Fall back to last-opened if no explicit path.
        if (string.IsNullOrEmpty(targetPath)
            && _lastOpenedByKind.TryGetValue(targetKind, out var remembered))
        {
            targetPath = remembered;
        }

        if (string.IsNullOrEmpty(targetPath))
            return;

        // Compute ancestor folder paths for expansion.
        var ancestors = GetAncestorPaths(targetPath);
        if (ancestors.Count > 0)
            _expandedFolders[targetKind] = new HashSet<string>(ancestors);

        // Set selection to the target asset.
        if (_leafMap.TryGetValue(targetKind, out var map)
            && map.TryGetValue(targetPath, out var asset))
        {
            Selection = asset;
        }
    }

    /// <summary>
    /// Returns all ancestor folder paths for the given relative path.
    /// For <c>"combat/patrol/Guard.bp.json"</c> returns <c>["combat", "combat/patrol"]</c>.
    /// For a single-segment path (no <c>/</c>) returns an empty collection.
    /// </summary>
    internal static IReadOnlyList<string> GetAncestorPaths(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return Array.Empty<string>();

        var segments = fullPath.Split('/');
        if (segments.Length <= 1)
            return Array.Empty<string>();

        var ancestors = new List<string>(segments.Length - 1);
        var accumulated = "";
        for (int i = 0; i < segments.Length - 1; i++)
        {
            accumulated = i == 0 ? segments[i] : accumulated + "/" + segments[i];
            ancestors.Add(accumulated);
        }
        return ancestors;
    }

    /// <summary>
    /// Recursively prunes a folder tree, keeping only leaves whose asset
    /// <c>Name</c> matches <see cref="_filter"/> (case-insensitive substring)
    /// and the ancestor folders needed to reach them.
    /// </summary>
    private FolderTreeNode PruneTree(FolderTreeNode node, AssetKind kind)
    {
        if (node.IsLeaf)
        {
            var asset = AssetForLeaf(kind, node);
            if (asset != null && asset.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            {
                return new FolderTreeNode(node.Name, node.FullPath, isLeaf: true,
                    Array.Empty<FolderTreeNode>());
            }
            return null!; // pruned
        }

        var keptChildren = new List<FolderTreeNode>();
        foreach (var child in node.Children)
        {
            var pruned = PruneTree(child, kind);
            if (pruned != null)
                keptChildren.Add(pruned);
        }

        if (keptChildren.Count == 0)
            return null!; // pruned — no matching descendants

        return new FolderTreeNode(node.Name, node.FullPath, isLeaf: false,
            keptChildren.AsReadOnly());
    }

    /// <summary>
    /// Returns the asset root base folder for <paramref name="kind"/>,
    /// or <see langword="null"/> for kinds with no Assets root
    /// (Blackboard, Utility — and Scenario in the future).
    /// </summary>
    /// <remarks>
    /// Wraps the <see cref="ArgumentOutOfRangeException"/> thrown by
    /// <see cref="AssetRoots.AssetsFor"/> for rootless kinds → <see langword="null"/>.
    /// </remarks>
    internal static string? BaseFolderFor(AssetKind kind)
    {
        try
        {
            return AssetRoots.AssetsFor(kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
