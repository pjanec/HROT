using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// A single node in the global menu trie. Represents one path segment.
/// Leaf nodes have an action (<see cref="OnClick"/> or <see cref="GetCheckedState"/>/<see cref="OnCheckedChanged"/>)
/// or are separators (<see cref="IsSeparator"/>). Intermediate nodes have only <see cref="Children"/>.
/// </summary>
/// <summary>
/// ⭐⭐⭐ <b><c>UXI-05</c> — ONE BINDING of a menu leaf, for one perspective.</b>
/// 📄 <c>docs/DESIGN_Cgf_Menu_Follows_Focus_Slice.md</c> §3 ①.
///
/// <para>🔒 <b>UXI-05, verbatim:</b> <i>"a leaf holds <b>bindings, not one action</b>"</i>. ⇒ the same
/// menu PATH can do different things depending on which perspective has focus, without a second menu
/// tree and without <c>if (host==…)</c>.</para>
///
/// <para>⚠ <b>Only the ACTION varies per perspective.</b> ⛔ <c>Shortcut</c>, <c>Icon</c>,
/// <c>GetEnabled</c> and <c>DynamicLabel</c> stay node-level — faithful to UXI-05's own record, and a
/// refinement this slice deliberately does not make.</para>
/// </summary>
/// <param name="Perspective">
/// ⭐ The perspective this binding serves, or <see langword="null"/> for the GLOBAL binding — the one used
/// when no perspective-specific binding matches.
/// </param>
public sealed record MenuBinding(
    string? Perspective,
    Action? OnClick,
    Func<bool>? GetChecked,
    Action<bool>? OnCheckedChanged);

public class MenuItemNode
{
    /// <summary>The display name for this menu segment.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// ⭐⭐ <b><c>UXI-05</c> — every binding registered on this leaf.</b> ⭐ At most one per perspective
    /// key *(last-write-wins per perspective)*; ⛔ the list is the source of truth, and the three
    /// properties below are a READ-ONLY projection of the global one.
    /// </summary>
    public List<MenuBinding> Bindings { get; } = new();

    /// <summary>
    /// ⭐⭐ Picks the binding for <paramref name="perspective"/>, else the GLOBAL one, else
    /// <see langword="null"/>.
    /// <para>⛔⛔ <b><see langword="null"/> means the leaf is NOT DRAWN</b> — ruling 49's *"absent, not
    /// greyed"*, by construction rather than by a per-host branch.</para>
    /// </summary>
    public MenuBinding? ResolveBinding(string? perspective)
    {
        MenuBinding? global = null;
        foreach (var b in Bindings)
        {
            if (b.Perspective == null) { global = b; continue; }
            if (string.Equals(b.Perspective, perspective, StringComparison.Ordinal)) return b;
        }
        return global;
    }

    /// <summary>⭐ The global binding, or <see langword="null"/>. The back-compat projection's source.</summary>
    private MenuBinding? GlobalBinding => ResolveBinding(null);

    // ⭐⭐⭐ BACKWARD COMPATIBILITY, and it is the migration-step-1 GATE, not a courtesy.
    // 🔒 UXI-05: *"editor menu byte-identical, all items global"*. ⇒ every existing registration becomes
    //    ONE global binding, and every existing READER of these three properties keeps working unchanged.
    // ⛔ READ-ONLY on purpose: the registry writes BINDINGS. A setter here would let a caller create an
    //   action the resolution path cannot see, which is the one way this model can silently half-apply.

    /// <summary>Invoked when a plain menu item is selected. ⭐ The GLOBAL binding's action.</summary>
    public Action? OnClick => GlobalBinding?.OnClick;

    /// <summary>Returns the current checked state for a checkable menu item. ⭐ The GLOBAL binding's.</summary>
    public Func<bool>? GetCheckedState => GlobalBinding?.GetChecked;

    /// <summary>Invoked with the new checked value when a checkable item is selected. ⭐ The GLOBAL binding's.</summary>
    public Action<bool>? OnCheckedChanged => GlobalBinding?.OnCheckedChanged;

    /// <summary>When <c>true</c>, this node renders as a visual separator line.</summary>
    public bool IsSeparator { get; set; }

    /// <summary>
    /// ⭐⭐ <c>CE-4096</c> (AQ108 D16) — this leaf is ALLOWED while the cluster is Idle (the <c>Start</c> perspective): the bar then draws
    /// only such leaves, and the start surface draws them as buttons through the SAME binding. Set by
    /// <see cref="GlobalMenuRegistry.RegisterItem"/>'s <c>allowedInIdle</c>; default <see langword="false"/>.
    /// </summary>
    public bool AllowedInIdle { get; set; }

    /// <summary>
    /// Optional keyboard shortcut text displayed next to the item (e.g. "Ctrl+S").
    /// When <c>null</c> or empty, no shortcut is shown. Backward-compatible addition (MTB-P2-T2).
    /// </summary>
    public string? Shortcut { get; set; }

    /// <summary>
    /// Optional delegate returning whether this item is currently enabled.
    /// When <c>false</c> the item is greyed out and unclickable.
    /// When <c>null</c> the item is always enabled. Backward-compatible addition (MTB-P2-T2).
    /// </summary>
    public Func<bool>? GetEnabled { get; set; }

    /// <summary>
    /// Optional per-frame label override. When non-null, <see cref="ResolveLabel"/>
    /// returns its invoked value instead of <see cref="Name"/>.
    /// Re-evaluated every frame (immediate mode), like <see cref="GetEnabled"/>.
    /// </summary>
    public Func<string>? DynamicLabel { get; set; }

    /// <summary>
    /// Returns the resolved display label for this menu node.
    /// When <see cref="DynamicLabel"/> is set, delegates to it; otherwise returns <see cref="Name"/>.
    /// </summary>
    public string ResolveLabel() => DynamicLabel?.Invoke() ?? Name;

    /// <summary>
    /// Optional semantic icon key (e.g. <c>"save"</c>, <c>"open"</c>) resolved by the
    /// <c>WindowManager.MenuIcons</c> resolver to a colored atlas sprite rendered in an aligned
    /// gutter. When <c>null</c>, no icon is shown (the gutter is still reserved if any sibling
    /// has one, so labels stay aligned). Backward-compatible addition.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>Child nodes keyed by path segment name.</summary>
    public Dictionary<string, MenuItemNode> Children { get; } = new();

    /// <summary>
    /// ⭐⭐ <c>CE-1443</c> (U2a, AQ108 §2.15a) — where this node sits among its SIBLINGS: ascending; ties keep registration order, so <c>0</c> everywhere (every menu but the <c>File</c> menu) is
    /// byte-identical to the insertion order this registry always drew. ⭐ Needed because the items of one menu are registered by several hosts and registrars at different times — the
    /// registration order is not a layout.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// ⭐⭐ <c>CE-1443</c> — optional per-frame visibility of this leaf or separator (immediate mode, like <see cref="GetEnabled"/>). When <see langword="false"/> the node is NOT DRAWN (not greyed): <see cref="GlobalMenuRegistry.Offers"/>
    /// — the ONE visibility test — reads it, so the bar, the Start surface, the model and <c>/panels</c> cannot disagree. <see langword="null"/> = always visible.
    /// </summary>
    public Func<bool>? GetVisible { get; set; }

    /// <summary>This node's children in DRAW order: by <see cref="Order"/>, then registration order. ⛔ Every draw / model walk uses this, never <see cref="Children"/>'s raw order.</summary>
    public IEnumerable<MenuItemNode> OrderedChildren()
    {
        var ordered = false;
        foreach (var c in Children.Values) if (c.Order != 0) { ordered = true; break; }
        return ordered ? Children.Values.OrderBy(c => c.Order) : Children.Values;   // LINQ OrderBy is stable: ties keep registration order
    }
}

/// <summary>
/// Trie-based registry for global application menu items.
/// Paths use <c>'/'</c> as separator (e.g. <c>"Tools/Radar/Show"</c>).
/// </summary>
public class GlobalMenuRegistry
{
    /// <summary>The trie root. Its <see cref="MenuItemNode.Children"/> are the top-level menu entries.</summary>
    public MenuItemNode Root { get; } = new() { Name = "<root>" };

    // ── Top-level order (CE-1026) ─────────────────────────────────────────────

    /// <summary>
    /// ⭐ CE-1026 — the conventional order of the TOP-LEVEL menus, left to right; ties keep registration order.
    /// <para>⭐ CE-4085 — <c>Settings</c> and <c>Perspective</c> are SUBMENUS of <c>View</c> (<see cref="SettingsMenuPath"/>, <see cref="PerspectiveSubmenu"/>), and <c>Help</c> is last, flush right.
    /// ⭐ CE-4114 — <c>Windows</c> is a submenu of <c>View</c> too (<see cref="WindowsSubmenu"/>), and <c>Help</c> is an ICON (no text). 🔒 User: a root segment NOT listed here is drawn
    /// AFTER the rightmost listed non-Help menu (<see cref="UnlistedRank"/>), registration order among themselves — never in the right-aligned group.
    /// ⛔ SUPERSEDED: the CE-1243 order … Debug · Perspective · Settings · Windows · Help, and the "unlisted menus rank just before Windows" anchor (<c>FirstTrailingMenu</c>).</para>
    /// </summary>
    public static readonly IReadOnlyList<string> TopLevelOrder = new[] { "File", "Edit", "View", "Scenario", "Tools", "Debug", "Help" };

    /// <summary>⭐ CE-4114 — the window manager's window list; a submenu of <c>View</c>: <c>View/Windows/…</c>.</summary>
    public const string WindowsMenu = "Windows";

    /// <summary>⭐ CE-4114 — the Windows submenu's path: <c>View/Windows</c>.</summary>
    public const string WindowsSubmenu = "View/" + WindowsMenu;

    /// <summary>⭐ CE-4085 — the one top-level menu drawn flush with the bar's RIGHT edge; also the last in <see cref="TopLevelOrder"/>.</summary>
    public const string RightAlignedMenu = "Help";

    /// <summary>⭐ CE-4085 — the tree path every host registers its settings under (a submenu of <c>View</c>): <c>View/Settings/…</c>.</summary>
    public const string SettingsMenuPath = "View/Settings";

    /// <summary>⭐ CE-4085 — the label of the perspective switcher; a submenu of <c>View</c>, no longer a top-level menu.</summary>
    public const string PerspectiveMenu = "Perspective";

    /// <summary>⭐ CE-4085 — the perspective switcher's path: <c>View/Perspective</c>.</summary>
    public const string PerspectiveSubmenu = "View/" + PerspectiveMenu;

    /// <summary>⭐ CE-4114 — the rank every UNLISTED top-level menu gets: just after the rightmost listed non-Help menu (so before Help).</summary>
    public static int UnlistedRank
    {
        get
        {
            int last = -1;
            for (int i = 0; i < TopLevelOrder.Count; i++)
                if (!IsRightAligned(TopLevelOrder[i])) last = i;
            return last * 10 + 5;
        }
    }

    /// <summary>The rank of a top-level menu named <paramref name="name"/> in <see cref="TopLevelOrder"/> (an unlisted name gets <see cref="UnlistedRank"/>).</summary>
    public static int TopLevelRank(string name)
    {
        for (int i = 0; i < TopLevelOrder.Count; i++)
            if (string.Equals(TopLevelOrder[i], name, StringComparison.Ordinal)) return i * 10;
        return UnlistedRank;
    }

    /// <summary>⭐ CE-4085 — true when the top-level menu <paramref name="name"/> is drawn flush right (only <see cref="RightAlignedMenu"/>).</summary>
    public static bool IsRightAligned(string name) => string.Equals(name, RightAlignedMenu, StringComparison.Ordinal);

    /// <summary>The top-level menus in bar order (rank, then registration order).</summary>
    public IReadOnlyList<MenuItemNode> TopLevelMenus()
        => Root.Children.Values.Select((node, i) => (node, i)).OrderBy(t => TopLevelRank(t.node.Name)).ThenBy(t => t.i).Select(t => t.node).ToList();

    /// <summary>
    /// Registers a plain menu item at the given path.
    /// Intermediate nodes are created as needed. Last-write-wins on re-registration.
    /// </summary>
    /// <param name="path">Slash-separated path (e.g. <c>"File/Open"</c>). Must not be empty.</param>
    /// <param name="onClick">Action to invoke when the item is selected.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    /// <param name="perspective">
    /// ⭐⭐ <b><c>UXI-05</c></b> — the perspective this action serves, or <see langword="null"/> *(the
    /// default)* for a GLOBAL item shown under every perspective.
    /// ⛔⛔ <b>The default is what keeps every existing call byte-identical</b>: one global binding, drawn
    /// exactly as before.
    /// </param>
    /// <param name="allowedInIdle">
    /// ⭐⭐ <c>CE-4096</c> (AQ108 D16) — the item is offered while the cluster is Idle (the <c>Start</c> perspective): the bar draws ONLY such
    /// items there, and the start surface draws them as buttons (<see cref="AllowedInIdleItems"/>). Default <see langword="false"/> keeps
    /// every existing call byte-identical.
    /// </param>
    public void RegisterItem(string path, Action onClick, string? perspective = null, bool allowedInIdle = false)
    {
        Bind(path, new MenuBinding(perspective, onClick, null, null));
        TraversePath(path).AllowedInIdle = allowedInIdle;
    }

    // ── Idle-only (CE-4096) ────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <c>CE-4096</c> — when this answers <see langword="true"/> (the window manager wires it to <c>IsStartShowing</c>) the bar's model
    /// holds ONLY allowed-in-Idle items: <see cref="Offers"/>, <see cref="CollectContributions"/> and the published snapshot all obey it,
    /// so the drawn bar, the model and <c>/panels</c> cannot disagree.
    /// </summary>
    public Func<bool>? IdleOnlyWhen { get; set; }

    /// <summary>⭐ The bar is in its Idle-only form right now.</summary>
    public bool IdleOnly => IdleOnlyWhen?.Invoke() ?? false;

    /// <summary>⭐⭐ The ONE visibility test of a tree leaf: it resolves a binding for <paramref name="perspective"/>, and — while
    /// <see cref="IdleOnly"/> — is allowed in Idle. ⛔ Every draw / model path asks this, never <c>ResolveBinding</c> alone.</summary>
    public bool Offers(MenuItemNode node, string? perspective)
        => node.ResolveBinding(perspective) != null && (node.GetVisible?.Invoke() ?? true) && (!IdleOnly || IsAllowedInIdle(node.AllowedInIdle));   // ⭐ CE-1443: + the node's own visibility

    /// <summary>
    /// ⭐ CE-1441 (audit #20) — THE one rule "this item is allowed in Idle": its flag says so. A tree leaf (<see cref="MenuItemNode.AllowedInIdle"/>),
    /// a contributed item (<see cref="ContextMenuItemDto.AllowedInIdle"/>, nullable) and every walk that asks (<see cref="Offers"/>,
    /// <see cref="AllowedInIdleItems"/>, <see cref="PruneToIdle"/>) go through it, so the bar, the start surface and the contributions can never disagree.
    /// </summary>
    internal static bool IsAllowedInIdle(bool? allowedInIdle) => allowedInIdle == true;

    /// <summary>One thing the start surface can offer: a menu leaf allowed in Idle, invoked through the very binding the menu uses.</summary>
    public sealed record IdleAction(string Path, Func<string> Label, Func<bool> Enabled, Action Invoke);

    /// <summary>
    /// ⭐⭐ <c>CE-4096</c> — every item marked allowed-in-Idle, in bar order: tree leaves (their global binding) and contributed items
    /// (<see cref="ContextMenuItemDto.AllowedInIdle"/>, invoked through the contributor's <c>OnAction</c>). ⭐ The start surface draws THESE
    /// and owns no action of its own.
    /// </summary>
    public IReadOnlyList<IdleAction> AllowedInIdleItems(string perspective = StartPerspective)
    {
        var list = new List<IdleAction>();
        foreach (var bar in BarMenus(CollectContributionsUnfiltered(perspective)))
        {
            if (bar.Node is { } top) WalkIdle(top, bar.Name, list);
            foreach (var (contributor, dto) in bar.Parts) WalkIdleDto(dto, bar.Name, contributor, list);
        }
        return list;

        void WalkIdle(MenuItemNode node, string path, List<IdleAction> into)
        {
            if (node.IsSeparator) return;
            if (node.Children.Count > 0)
            {
                foreach (var c in node.OrderedChildren()) WalkIdle(c, path + "/" + c.Name, into);
                return;
            }
            if (!IsAllowedInIdle(node.AllowedInIdle) || !(node.GetVisible?.Invoke() ?? true) || node.ResolveBinding(perspective)?.OnClick is not { } click) return;
            into.Add(new IdleAction(path, node.ResolveLabel, () => node.GetEnabled?.Invoke() ?? true, click));
        }

        static void WalkIdleDto(ContextMenuItemDto dto, string path, MenuContributor contributor, List<IdleAction> into)
        {
            if (dto.IsSeparator == true) return;
            if (dto.Children is { Length: > 0 } children)
            {
                foreach (var c in children) WalkIdleDto(c, path + "/" + c.Label, contributor, into);
                return;
            }
            if (!IsAllowedInIdle(dto.AllowedInIdle)) return;
            var id = dto.Id;
            var enabled = dto.Enabled ?? true;
            into.Add(new IdleAction(path + (path.EndsWith("/" + dto.Label) ? "" : "/" + dto.Label), () => dto.Label ?? "", () => enabled, () => contributor.OnAction(id)));
        }
    }

    /// <summary>The perspective name the Idle form is evaluated against (the window manager's <c>StartPerspectiveName</c>).</summary>
    public const string StartPerspective = "Start";

    /// <summary>Keeps only allowed-in-Idle leaves (and the submenus holding one); separators go.</summary>
    internal static IReadOnlyList<ContextMenuItemDto> PruneToIdle(IReadOnlyList<ContextMenuItemDto> items)
    {
        var kept = new List<ContextMenuItemDto>();
        foreach (var dto in items)
        {
            if (dto.IsSeparator == true) continue;
            if (dto.Children is { Length: > 0 } children)
            {
                var sub = PruneToIdle(children);
                if (sub.Count == 0) continue;
                kept.Add(new ContextMenuItemDto
                {
                    Id = dto.Id, Label = dto.Label, Icon = dto.Icon, Enabled = dto.Enabled, Style = dto.Style, Shortcut = dto.Shortcut,
                    Tooltip = dto.Tooltip, Priority = dto.Priority, IsChecked = dto.IsChecked, AllowedInIdle = dto.AllowedInIdle,
                    Children = sub.ToArray(),
                });
            }
            else if (IsAllowedInIdle(dto.AllowedInIdle)) kept.Add(dto);
        }
        return kept;
    }

    /// <summary>
    /// Registers a checkable menu item at the given path.
    /// Last-write-wins on re-registration.
    /// </summary>
    /// <param name="path">Slash-separated path. Must not be empty.</param>
    /// <param name="getChecked">Returns the current checked state.</param>
    /// <param name="onChanged">Invoked with the new checked value when selected.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    /// <param name="perspective">⭐ See <see cref="RegisterItem"/> — <see langword="null"/> is global.</param>
    public void RegisterCheckableItem(
        string path, Func<bool> getChecked, Action<bool> onChanged, string? perspective = null)
        => Bind(path, new MenuBinding(perspective, null, getChecked, onChanged));

    /// <summary>
    /// Registers a separator at the given path.
    /// Last-write-wins on re-registration.
    /// </summary>
    /// <param name="path">Slash-separated path. Must not be empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    /// <param name="perspective">
    /// ⚠ Accepted for signature symmetry with the other two registrars, ⛔ but a separator carries no
    /// action, so a perspective-scoped one is meaningless — it is recorded as a binding purely so the
    /// draw path can skip a separator whose perspective does not match.
    /// </param>
    public void RegisterSeparator(string path, string? perspective = null)
    {
        var node = TraversePath(path);
        node.Bindings.Clear();
        node.Bindings.Add(new MenuBinding(perspective, null, null, null));
        node.IsSeparator = true;
    }

    // ── Layout rules (CE-1443) ─────────────────────────────────────────────────

    private readonly Dictionary<string, (int Order, Func<bool>? Visible)> _layout = new(StringComparer.Ordinal);

    /// <summary>
    /// ⭐⭐ <c>CE-1443</c> (U2a, AQ108 §2.15a) — declares where the node at <paramref name="path"/> sits among its siblings (<paramref name="order"/>) and when it is DRAWN (<paramref name="visible"/>; <see langword="null"/> = always).
    /// ⭐ It does not matter whether the node exists yet: the several hosts and registrars that fill one menu register at different times, so the rule is kept and applied when the node is created, or at once when it
    /// is already there. ⛔ It creates no node (a rule for an item no host registers leaves no phantom row in the bar or in <c>/panels</c>). Last write wins.
    /// </summary>
    public void SetLayout(string path, int order, Func<bool>? visible = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Menu item path must not be empty.", nameof(path));
        _layout[path] = (order, visible);
        var node = MenuCommandAdapter.FindNode(Root, path);
        if (node is null) return;
        node.Order = order;
        node.GetVisible = visible;
    }

    // ── Contributors (CE-1243 MB-2) ────────────────────────────────────────────

    private readonly List<MenuContributor> _contributors = new();

    /// <summary>
    /// ⭐⭐ <c>CE-1243</c> MB-2 (docs/UX/UX_Feature_Menu_Follows_Focus.md §MB) — menus COMPUTED EACH FRAME (the window list, the
    /// perspectives, a host's gizmo menus), merged into the bar by their top-level label (a contributed <c>View</c> lands in the one View
    /// menu); a click on a contributed row goes to <paramref name="onAction"/> with the row's id. <paramref name="perspective"/> null =
    /// global (UXI-05). Last-write-wins on <paramref name="id"/>. ⛔ SUPERSEDED: the window manager's own DTO menus and each host's own
    /// <c>BeginMainMenuBar</c> block.
    /// </summary>
    public void RegisterContributor(string id, Func<IReadOnlyList<ContextMenuItemDto>> items, Action<int> onAction, string? perspective = null)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("A contributor needs an id.", nameof(id));
        var c = new MenuContributor(id, perspective, items ?? throw new ArgumentNullException(nameof(items)),
                                    onAction ?? throw new ArgumentNullException(nameof(onAction)));
        int at = _contributors.FindIndex(x => x.Id == id);
        if (at >= 0) _contributors[at] = c; else _contributors.Add(c);
    }

    /// <summary>The registered contributors, in registration order.</summary>
    public IReadOnlyList<MenuContributor> Contributors => _contributors;

    /// <summary>
    /// ⭐ Asks every contributor bound to <paramref name="currentPerspective"/> (or global) for its menus — ONCE a frame: a contributor may
    /// CONSUME what it returns (a gizmo layer's main-menu feed does), so the bar and the snapshot share this one answer.
    /// </summary>
    public IReadOnlyList<MenuContribution> CollectContributions(string currentPerspective)
    {
        var all = CollectContributionsUnfiltered(currentPerspective);
        if (!IdleOnly) return all;
        // ⭐ CE-4096 — the Idle-only bar: each contribution pruned to its allowed-in-Idle items (a contributor with none drops out).
        var list = new List<MenuContribution>();
        foreach (var c in all)
        {
            var kept = PruneToIdle(c.Items);
            if (kept.Count > 0) list.Add(new MenuContribution(c.Contributor, kept));
        }
        return list;
    }

    private IReadOnlyList<MenuContribution> CollectContributionsUnfiltered(string currentPerspective)
    {
        var list = new List<MenuContribution>();
        foreach (var c in _contributors)
        {
            if (c.Perspective != null && c.Perspective != currentPerspective) continue;
            var items = c.Items();
            if (items is { Count: > 0 }) list.Add(new MenuContribution(c, items));
        }
        return list;
    }

    /// <summary>
    /// ⭐ The bar's top-level menus, in bar order: every tree menu and every contributed top-level label, merged by name (rank, then the
    /// tree's registration order, then the contributors').
    /// </summary>
    public IReadOnlyList<BarMenu> BarMenus(IReadOnlyList<MenuContribution> contributions)
    {
        var byName = new Dictionary<string, BarMenu>(StringComparer.Ordinal);
        var order = new List<BarMenu>();
        BarMenu Get(string name)
        {
            if (!byName.TryGetValue(name, out var m)) { m = new BarMenu(name); byName[name] = m; order.Add(m); }
            return m;
        }
        foreach (var node in Root.Children.Values) Get(node.Name).Node = node;
        foreach (var contribution in contributions)
            foreach (var top in contribution.Items)
                if (!string.IsNullOrEmpty(top.Label)) Get(top.Label!).Parts.Add((contribution.Contributor, top));
        return order.Select((m, i) => (m, i)).OrderBy(t => TopLevelRank(t.m.Name)).ThenBy(t => t.i).Select(t => t.m).ToList();
    }

    // ── Observability (UXI-05 item ⑤) ─────────────────────────────────────────

    /// <summary>⭐ The panel id every host publishes this singleton under.</summary>
    public const string PanelIdLiteral = "global_menu";

    /// <summary>⭐⭐ The KIND — identical on every host, so cross-host conformance groups by it.</summary>
    public const string PanelKindLiteral = "global-menu";

    /// <summary>
    /// ⭐⭐ Flattens the trie into the leaves a host actually offers, evaluated against
    /// <paramref name="currentPerspective"/>.
    ///
    /// <para>⭐⭐⭐ <b>Why this had to exist before item ⑤ could assert anything.</b> 📐 Measured: the
    /// toolbar has published a panel model since slice 2, the MENU published <b>nothing</b> — so
    /// *"which File items does CGF offer?"* was unanswerable headlessly, exactly the gap
    /// <see cref="MainToolbarPanelViewModel"/> closed for the toolbar. ⛔ A conformance verdict on an
    /// unpublished surface is not a verdict.</para>
    ///
    /// <para>⚠ <b>Leaves only, and PATHS not pixels.</b> Intermediate nodes are structure, not offers;
    /// a submenu appears in the dump only through the leaves beneath it. ⭐ <c>visible</c> carries the
    /// UXI-05 resolution — ⛔ *"registered for another perspective"* and *"never registered"* are
    /// different claims, and telling them apart is the point.</para>
    /// </summary>
    public GlobalMenuPanelViewModel BuildViewModel(string currentPerspective)
        => BuildViewModel(currentPerspective, CollectContributions(currentPerspective));

    /// <summary>⭐ CE-1243 — as above, over contributions already collected this frame (the bar's own answer).</summary>
    public GlobalMenuPanelViewModel BuildViewModel(string currentPerspective, IReadOnlyList<MenuContribution> contributions)
    {
        var items = new List<GlobalMenuItemView>();
        // ⭐ CE-1026: the top level in BAR order, so the dump lists what the bar draws in the order it draws it.
        // ⭐ CE-1243: contributed menus are part of the bar, so they are part of the dump (scope = the contributor's perspective).
        foreach (var bar in BarMenus(contributions))
        {
            if (bar.Node is { } top)
            {
                var single = new MenuItemNode { Name = Root.Name };
                single.Children[top.Name] = top;
                Walk(single, "", items, currentPerspective);
            }
            foreach (var (contributor, dto) in bar.Parts)
                WalkDto(dto, "", items, contributor.Perspective ?? "*");
        }
        return new GlobalMenuPanelViewModel(
            PanelIdLiteral, PanelKindLiteral, currentPerspective, items);

        static void WalkDto(ContextMenuItemDto dto, string prefix, List<GlobalMenuItemView> into, string scope)
        {
            var path = prefix.Length == 0 ? dto.Label ?? "" : $"{prefix}/{dto.Label}";
            if (dto.Children is { Length: > 0 } children)
            {
                foreach (var c in children) WalkDto(c, path, into, scope);
                return;
            }
            bool separator = string.IsNullOrEmpty(dto.Label) || dto.IsSeparator == true;
            into.Add(new GlobalMenuItemView(path, separator ? "separator" : "item", new[] { scope }, Visible: true));
        }

        void Walk(MenuItemNode node, string prefix, List<GlobalMenuItemView> into, string perspective)
        {
            foreach (var child in node.OrderedChildren())
            {
                var path = prefix.Length == 0 ? child.Name : $"{prefix}/{child.Name}";

                if (child.Children.Count > 0)
                {
                    Walk(child, path, into, perspective);
                    continue;
                }

                var scopes = new List<string>(child.Bindings.Count);
                foreach (var b in child.Bindings) scopes.Add(b.Perspective ?? "*");
                scopes.Sort(StringComparer.Ordinal);

                into.Add(new GlobalMenuItemView(
                    Path:        path,
                    Kind:        child.IsSeparator ? "separator" : "item",
                    Scopes:      scopes,
                    // ⭐ THE SAME expression RenderGlobalMenu filters on — ⛔ not a re-derivation.
                    Visible:     Offers(child, perspective)));
            }
        }
    }

    /// <summary>
    /// ⭐⭐⭐ Declares and registers the menu snapshot. ⚠ Called from the render loop <b>unconditionally</b>,
    /// for the same reason <see cref="MainToolbarManager.PublishSnapshot"/> is: ⛔ publishing only when the
    /// bar draws would make *"this host offers no menu items"* indistinguishable from *"this host's menu
    /// was never instrumented"*.
    /// </summary>
    public void PublishSnapshot(string currentPerspective)
        => PublishSnapshot(currentPerspective, CollectContributions(currentPerspective));

    /// <summary>⭐ CE-1243 — as above, over the contributions the bar collected this frame.</summary>
    public void PublishSnapshot(string currentPerspective, IReadOnlyList<MenuContribution> contributions)
    {
        PanelSnapshot.DeclareInstrumented(PanelIdLiteral);
        if (!PanelSnapshot.CaptureEnabled) return;

        PanelSnapshot.Register(BuildViewModel(currentPerspective, contributions));
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Adds or REPLACES the binding for <paramref name="binding"/>'s perspective.
    /// <para>⚠ <b>Last-write-wins PER PERSPECTIVE</b>, not per node — that is the whole point: re-registering
    /// the global action must not wipe a perspective-specific one, and vice versa. ⛔ The pre-UXI-05
    /// behaviour *(last-write-wins per node)* is preserved exactly for the single-binding case, which is
    /// every existing call.</para>
    /// </summary>
    private void Bind(string path, MenuBinding binding)
    {
        var node = TraversePath(path);
        node.IsSeparator = false;

        for (int i = 0; i < node.Bindings.Count; i++)
        {
            if (node.Bindings[i].Perspective == binding.Perspective)
            {
                node.Bindings[i] = binding;
                return;
            }
        }
        node.Bindings.Add(binding);
    }

    /// <summary>
    /// Traverses (and creates) trie nodes for <paramref name="path"/>.
    /// Returns the leaf node. Sets <see cref="MenuItemNode.Name"/> on each created node.
    /// </summary>
    private MenuItemNode TraversePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Menu item path must not be empty.", nameof(path));

        var segments = path.Split('/');
        var current = Root;
        var soFar = "";

        foreach (var segment in segments)
        {
            // Skip empty segments (e.g. trailing slash produces an empty last segment).
            if (segment.Length == 0) continue;
            soFar = soFar.Length == 0 ? segment : soFar + "/" + segment;

            if (!current.Children.TryGetValue(segment, out var child))
            {
                child = new MenuItemNode { Name = segment };
                if (_layout.TryGetValue(soFar, out var rule)) { child.Order = rule.Order; child.GetVisible = rule.Visible; }   // ⭐ CE-1443: a layout rule declared BEFORE the node exists
                current.Children[segment] = child;
            }

            current = child;
        }

        return current;
    }
}

/// <summary>
/// ⭐⭐⭐ <b>What the GLOBAL MENU offers right now — the shell's menu bar made machine-readable.</b>
/// 📄 <c>docs/DESIGN_Cgf_Menu_Follows_Focus_Slice.md</c> §3 ⑤.
///
/// <para>⭐⭐ The direct mirror of <see cref="MainToolbarPanelViewModel"/>, and for the same reason: both
/// hosts now register their File items from ONE table
/// *(<c>CgfEditorShellToolbar.Layout</c>)*, so cross-host conformance must be able to READ the result.
/// ⛔ Before this the menu published nothing at all.</para>
/// </summary>
/// <param name="PanelId">⭐ A singleton per host ⇒ the declared literal <c>global_menu</c>.</param>
/// <param name="PanelKind">⭐ <c>global-menu</c> — the SAME kind on every host, so conformance groups it.</param>
/// <param name="CurrentPerspective">The perspective the visibility filter was evaluated against.</param>
/// <param name="Items">Every LEAF, in trie walk order. ⛔ Intermediate nodes are structure, not offers.</param>
public sealed record GlobalMenuPanelViewModel(
    string PanelId,
    string PanelKind,
    string CurrentPerspective,
    IReadOnlyList<GlobalMenuItemView> Items) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>⭐ One menu leaf as the dump sees it.</summary>
/// <param name="Path">The full slash path — the stable name a rail asserts on.</param>
/// <param name="Kind"><c>item</c> or <c>separator</c>.</param>
/// <param name="Scopes">
/// ⭐⭐ Every perspective this leaf carries a binding for, sorted; <c>*</c> is the GLOBAL binding.
/// ⚠ This is what makes UXI-05's model visible to a rail: ⛔ a leaf bound twice *(global + one
/// perspective)* is a different thing from a leaf bound once, and only this field says which.
/// </param>
/// <param name="Visible">Whether the CURRENT perspective resolves a binding — i.e. whether it draws.</param>
public sealed record GlobalMenuItemView(
    string Path, string Kind, IReadOnlyList<string> Scopes, bool Visible);

/// <summary>⭐ CE-1243 MB-2 — a source of menus computed each frame (see <see cref="GlobalMenuRegistry.RegisterContributor"/>).</summary>
public sealed record MenuContributor(
    string Id, string? Perspective, Func<IReadOnlyList<ContextMenuItemDto>> Items, Action<int> OnAction);

/// <summary>⭐ CE-1243 — what one contributor offered this frame.</summary>
public sealed record MenuContribution(MenuContributor Contributor, IReadOnlyList<ContextMenuItemDto> Items);

/// <summary>⭐ CE-1243 — one top-level menu of the bar: the tree's node of that name (if any) and every contributed top-level item of that
/// label, drawn as ONE menu.</summary>
public sealed class BarMenu
{
    public BarMenu(string name) => Name = name;
    public string Name { get; }
    public MenuItemNode? Node { get; set; }
    public List<(MenuContributor Contributor, ContextMenuItemDto Item)> Parts { get; } = new();

    /// <summary>⭐ CE-4085 — drawn flush with the bar's right edge (<see cref="GlobalMenuRegistry.IsRightAligned"/>); the model the draw path obeys.</summary>
    public bool RightAligned => GlobalMenuRegistry.IsRightAligned(Name);

    /// <summary>⭐ CE-4114 — drawn as its ICON only, no label text (Help); the model the draw path obeys.</summary>
    public bool IconOnly => RightAligned;

    /// <summary>⭐ CE-4114 — the label the bar hands to ImGui: the name, or <c>##Name</c> (an id with NO visible text) for an icon-only menu.</summary>
    public string DrawLabel => IconOnly ? "##" + Name : Name;
}
