using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;

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
}

/// <summary>
/// Trie-based registry for global application menu items.
/// Paths use <c>'/'</c> as separator (e.g. <c>"Tools/Radar/Show"</c>).
/// </summary>
public class GlobalMenuRegistry
{
    /// <summary>The trie root. Its <see cref="MenuItemNode.Children"/> are the top-level menu entries.</summary>
    public MenuItemNode Root { get; } = new() { Name = "<root>" };

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
    public void RegisterItem(string path, Action onClick, string? perspective = null)
        => Bind(path, new MenuBinding(perspective, onClick, null, null));

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
    {
        var items = new List<GlobalMenuItemView>();
        Walk(Root, "", items, currentPerspective);
        return new GlobalMenuPanelViewModel(
            PanelIdLiteral, PanelKindLiteral, currentPerspective, items);

        static void Walk(MenuItemNode node, string prefix, List<GlobalMenuItemView> into, string perspective)
        {
            foreach (var child in node.Children.Values)
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
                    Visible:     child.ResolveBinding(perspective) != null));
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
    {
        PanelSnapshot.DeclareInstrumented(PanelIdLiteral);
        if (!PanelSnapshot.CaptureEnabled) return;

        PanelSnapshot.Register(BuildViewModel(currentPerspective));
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

        foreach (var segment in segments)
        {
            // Skip empty segments (e.g. trailing slash produces an empty last segment).
            if (segment.Length == 0) continue;

            if (!current.Children.TryGetValue(segment, out var child))
            {
                child = new MenuItemNode { Name = segment };
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
