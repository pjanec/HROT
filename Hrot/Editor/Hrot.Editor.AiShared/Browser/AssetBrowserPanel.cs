using ImGuiNET;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Bitmask filter for <see cref="AssetKind"/> values used by
/// <see cref="AssetBrowserPanelOptions"/> to control which tabs appear.
/// </summary>
/// <remarks>
/// <see cref="Scenario"/> is reserved for MTB-P5-T2 — the flag is defined but
/// not yet mapped to an <see cref="AssetKind"/> enum value.
/// </remarks>
[Flags]
public enum AssetKindFilter
{
    None        = 0,
    Scenario    = 1,      // reserved — not yet wired (no AssetKind.Scenario)
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
            $"Unknown {nameof(AssetKind)} value: {kind}")
    };

    /// <summary>
    /// Returns the permitted <see cref="AssetKind"/> values from <paramref name="filter"/>,
    /// in enum declaration order.
    /// </summary>
    /// <remarks>
    /// <see cref="AssetKindFilter.Scenario"/> is <b>not</b> included — it is reserved
    /// until <c>AssetKind.Scenario</c> exists (MTB-P5-T2).
    /// </remarks>
    public static IReadOnlyList<AssetKind> PermittedKinds(AssetKindFilter filter)
    {
        var kinds = new List<AssetKind>(5);
        if (filter.HasFlag(AssetKindFilter.Blueprint))  kinds.Add(AssetKind.Blueprint);
        if (filter.HasFlag(AssetKindFilter.BTree))      kinds.Add(AssetKind.BTree);
        if (filter.HasFlag(AssetKindFilter.Hsm))        kinds.Add(AssetKind.Hsm);
        if (filter.HasFlag(AssetKindFilter.Blackboard)) kinds.Add(AssetKind.Blackboard);
        if (filter.HasFlag(AssetKindFilter.Utility))    kinds.Add(AssetKind.Utility);
        // Scenario reserved for MTB-P5-T2
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
    /// chips is shown. <b>Stored but not yet wired (MTB-P4-T4).</b>
    /// </summary>
    public bool ShowAllTab { get; init; } = true;

    /// <summary>
    /// The kind to activate on first draw. <b>Stored but not yet wired (MTB-P4-T5).</b>
    /// </summary>
    public AssetKind? InitialKind { get; init; }

    /// <summary>
    /// The relative-to-root path to auto-expand and select on first draw.
    /// <b>Stored but not yet wired (MTB-P4-T5).</b>
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

    // Per-kind cache rebuilt on catalog Changed.
    private readonly Dictionary<AssetKind, FolderTreeNode> _trees = new();
    private readonly Dictionary<AssetKind, Dictionary<string, IEditableAsset>> _leafMap = new();

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

    /// <summary>
    /// Creates a new <see cref="AssetBrowserPanel"/>.
    /// </summary>
    /// <param name="catalog">The asset catalog (never <see langword="null"/>).</param>
    /// <param name="icons">The icon provider for resolving kind-icon keys (never <see langword="null"/>).</param>
    /// <param name="options">Panel options (never <see langword="null"/>).</param>
    public AssetBrowserPanel(IAssetCatalog catalog, IIconProvider icons, AssetBrowserPanelOptions options)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        Tabs = AssetKindFilterMapping.PermittedKinds(options.Kinds);

        RebuildTrees();
        _catalog.Changed += OnCatalogChanged;
    }

    /// <summary>
    /// Returns the folder tree for <paramref name="kind"/>, built from
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
    /// Returns the <see cref="IIconProvider"/> key for the given asset's kind icon,
    /// resolved via <see cref="AssetKindIcons.GetIconKey"/>.
    /// </summary>
    public string RowIconKey(IEditableAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        return AssetKindIcons.GetIconKey(asset.Kind);
    }

    /// <summary>
    /// Sets <see cref="Selection"/> to <paramref name="asset"/> (single-click highlight).
    /// Pass <see langword="null"/> to clear selection.
    /// </summary>
    public void SelectAsset(IEditableAsset? asset)
    {
        Selection = asset;
    }

    /// <summary>
    /// Raises the <see cref="AssetActivated"/> event with <paramref name="asset"/>
    /// (double-click / Enter). The panel performs no side effects.
    /// </summary>
    public void ActivateAsset(IEditableAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        AssetActivated?.Invoke(asset);
    }

    // ── Draw ───────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the panel content via ImGui: per-kind tabs, a folder tree for
    /// the active tab, and rows with kind icons.
    /// </summary>
    public void DrawContent()
    {
        if (Tabs.Count == 0)
            return;

        // ── Tab bar ─────────────────────────────────────────────────
        if (ImGui.BeginTabBar("##AssetBrowserTabs"))
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                var kind = Tabs[i];
                var label = kind.ToString();
                bool tabOpen = true;

                ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
                if (ImGui.BeginTabItem(label, ref tabOpen, flags))
                {
                    _activeTabIndex = i;
                    DrawKindTab(kind);
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawKindTab(AssetKind kind)
    {
        var tree = TreeFor(kind);
        if (tree.Children.Count == 0)
        {
            ImGui.TextDisabled("No assets");
            return;
        }

        foreach (var child in tree.Children)
        {
            DrawTreeNode(kind, child);
        }
    }

    private void DrawTreeNode(AssetKind kind, FolderTreeNode node)
    {
        if (node.IsLeaf)
        {
            DrawLeafRow(kind, node);
            return;
        }

        // Folder node
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow
                                   | ImGuiTreeNodeFlags.OpenOnDoubleClick
                                   | ImGuiTreeNodeFlags.DefaultOpen;

        bool isOpen = ImGui.TreeNodeEx(node.Name, flags);

        if (isOpen)
        {
            foreach (var child in node.Children)
            {
                DrawTreeNode(kind, child);
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
            // Draw a small icon-sized image; rely on the provider's handle info.
            // For simplicity, render the icon key as text with a prefix.
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

    // ── Internals ──────────────────────────────────────────────────────

    private void OnCatalogChanged()
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
