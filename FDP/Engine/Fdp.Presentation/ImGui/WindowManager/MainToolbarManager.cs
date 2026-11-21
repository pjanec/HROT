using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using ImGuiNET;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐⭐⭐ <b>What the MAIN TOOLBAR offers right now — the shell's toolbar made machine-readable.</b>
/// 📄 <c>DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md</c> §6 item ⑤ · §7 *(the standing reminder)*.
///
/// <para>⛔⛔ <b>The gap it closes, measured `2026-08-25`:</b> <see cref="MainToolbarManager"/> renders every
/// entry through an opaque <c>Action</c> delegate and published <b>nothing</b>. ⇒ MCP could read every
/// docked panel and <b>not</b> the toolbar — so *"does this host offer the Reload button?"* was
/// unanswerable headlessly, on either host.</para>
///
/// <para>⭐⭐ <b>Entry IDS, not entry pixels.</b> ⛔ The render delegates draw arbitrary ImGui and cannot be
/// introspected; ⭐ what IS structural — and what §7 actually asks about — is <b>which entries this host
/// registered and which of them the active perspective shows</b>. ⇒ the dump carries the ids, their sort
/// order, their perspective filter and their visibility, ⛔ not a picture.</para>
///
/// <para>⚠ <b><c>visible</c> is the field that means something.</b> An entry registered for another
/// perspective is present-but-hidden, which is a different claim from *"this host never registered it"* —
/// 📌 and telling those two apart is the whole reason a later feature slice can assert
/// *"its toolbar affordance is present and SAME on CGF"*.</para>
/// </summary>
/// <param name="PanelId">⭐ A singleton per host ⇒ the declared literal <c>main_toolbar</c>.</param>
/// <param name="PanelKind">⭐ <c>main-toolbar</c> — the SAME kind on every host, so conformance groups it.</param>
/// <param name="CurrentPerspective">The perspective the visibility filter was evaluated against.</param>
/// <param name="Entries">Every registered item, in render order.</param>
public sealed record MainToolbarPanelViewModel(
    string PanelId,
    string PanelKind,
    string CurrentPerspective,
    IReadOnlyList<MainToolbarEntryView> Entries) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>⭐ One toolbar item as the dump sees it.</summary>
/// <param name="Id">The registration id — the stable name a rail asserts on.</param>
/// <param name="Kind"><c>entry</c> or <c>separator</c>.</param>
/// <param name="SortOrder">Render order; ⚠ a reshuffle is a UX change, not noise.</param>
/// <param name="Perspective">The perspective filter, or <c>null</c> for a global item.</param>
/// <param name="Visible">Whether the CURRENT perspective shows it.</param>
public sealed record MainToolbarEntryView(
    string Id, string Kind, int SortOrder, string? Perspective, bool Visible);

/// <summary>
/// Manages a persistent top-anchored toolbar rendered as a band directly under
/// the main menu bar. Subsystems register named entries (sorted by <c>sortOrder</c>)
/// with a declared height; the manager renders them left-to-right.
/// Entries can be restricted to a specific perspective or marked as global (always visible).
/// Registered separators draw vertical dividers between entry groups.
/// <para>
/// <b>Jitter-free height (§4.1.1):</b> <see cref="Height"/> = max <c>declaredHeight</c>
/// over ALL registered entries (not just currently visible ones), so the value is
/// constant across perspective switches — the central dockspace never bounces.
/// </para>
/// </summary>
public sealed class MainToolbarManager
{
    // ── Inner types ──────────────────────────────────────────────────────────

    private abstract class ToolbarItem
    {
        public string Id = "";
        public int SortOrder;
        /// <summary>
        /// Perspective filter. <c>null</c> = global (always visible).
        /// Any non-null value restricts this item to the named perspective.
        /// </summary>
        public string? Perspective;
    }

    private sealed class EntryItem : ToolbarItem
    {
        public float DeclaredHeight;
        public Action RenderDelegate = null!;
    }

    private sealed class SeparatorItem : ToolbarItem { }

    // ── Fields ──────────────────────────────────────────────────────────────

    private readonly List<ToolbarItem> _items = new();
    private bool _needsSort;
    private float _maxDeclaredHeight;

    // Very dark gray used as the toolbar window background so it is
    // visually distinct from normal window client-area backgrounds (mirrors StatusBarBg).
    private static readonly Vector4 ToolbarBg = new(0.12f, 0.12f, 0.12f, 1f);

    // ── Properties ──────────────────────────────────────────────────────────

    /// <summary>
    /// Toolbar height in pixels = max <c>declaredHeight</c> over ALL registered entries.
    /// This is known immediately after registration and does not depend on which
    /// entries are currently visible — constant across perspective switches.
    /// </summary>
    public float Height => _maxDeclaredHeight;

    /// <summary>
    /// Default height for a toolbar entry, matching the menu-bar frame height
    /// when an ImGui context is active. Falls back to a sensible constant when
    /// no context exists (e.g. during headless unit tests) so that
    /// <see cref="Height"/> remains &gt; 0 regardless.
    /// </summary>
    /// <remarks>
    /// <b>BATCH-25:</b> Use this instead of a hardcoded <c>64f</c> for
    /// <c>declaredHeight</c> and icon rendering so the toolbar fits inside the
    /// menu-bar row at native resolution.
    /// </remarks>
    public static float DefaultEntryHeight
    {
        get
        {
            var ctx = ImGui.GetCurrentContext();
            return ctx != IntPtr.Zero ? ImGui.GetFrameHeight() : 20f;
        }
    }

    /// <summary>
    /// Exposed for testability. Returns the ordered list of visible items
    /// (entries and separators) for a given perspective, without performing
    /// any ImGui draw calls. This is the headless-test seam.
    /// </summary>
    /// <summary>
    /// Public diagnostic/test accessor: <c>true</c> when an <b>entry</b> (not a separator) with the given
    /// <paramref name="id"/> is registered. Lets out-of-assembly tests assert toolbar composition without the
    /// internal <see cref="GetVisibleItemPlan"/> seam.
    /// </summary>
    public bool ContainsEntry(string id)
    {
        foreach (var item in _items)
            if (item is EntryItem && item.Id == id)
                return true;
        return false;
    }

    internal IReadOnlyList<(string Id, bool IsSeparator, int SortOrder)> GetVisibleItemPlan(string perspective)
    {
        EnsureSorted();

        var result = new List<(string, bool, int)>();
        foreach (var item in _items)
        {
            bool visible = item.Perspective == null || item.Perspective == perspective;
            if (!visible) continue;

            result.Add((item.Id, item is SeparatorItem, item.SortOrder));
        }

        return result;
    }

    // ── Registration ────────────────────────────────────────────────────────

    /// <summary>
    /// Registers (or replaces) a named toolbar entry.
    /// </summary>
    /// <param name="id">Unique entry identifier. Duplicate ids replace the previous entry (last-write-wins).</param>
    /// <param name="sortOrder">Ascending sort order; lower values render further left.</param>
    /// <param name="declaredHeight">
    /// The fixed vertical pixels this entry needs. Used to compute <see cref="Height"/>
    /// — the toolbar does not measure entries per-frame (§4.1.1).
    /// </param>
    /// <param name="renderDelegate">
    /// Delegate called each frame to render this entry's ImGui content.
    /// Must not be <c>null</c>.
    /// </param>
    /// <param name="perspective">
    /// Optional perspective name. When <c>null</c> (default) the entry is global and always
    /// rendered. When set, the entry is only rendered when the active perspective matches.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="renderDelegate"/> is <c>null</c>.</exception>
    public void RegisterEntry(string id, int sortOrder, float declaredHeight, Action renderDelegate, string? perspective = null)
    {
        ArgumentNullException.ThrowIfNull(renderDelegate);

        var entry = new EntryItem
        {
            Id = id,
            SortOrder = sortOrder,
            DeclaredHeight = declaredHeight,
            Perspective = perspective,
            RenderDelegate = renderDelegate
        };

        ReplaceOrAdd(entry);
        RecalcHeight();
    }

    /// <summary>
    /// Registers (or replaces) a vertical separator in the toolbar.
    /// Separators participate in ordering but have no render delegate.
    /// </summary>
    /// <param name="id">Unique separator identifier.</param>
    /// <param name="sortOrder">Ascending sort order.</param>
    /// <param name="perspective">Optional perspective filter.</param>
    public void RegisterSeparator(string id, int sortOrder, string? perspective = null)
    {
        var sep = new SeparatorItem
        {
            Id = id,
            SortOrder = sortOrder,
            Perspective = perspective
        };

        ReplaceOrAdd(sep);
    }

    private void ReplaceOrAdd(ToolbarItem newItem)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id == newItem.Id)
            {
                _items[i] = newItem;
                _needsSort = true;
                return;
            }
        }

        _items.Add(newItem);
        _needsSort = true;
    }

    private void RecalcHeight()
    {
        float max = 0f;
        foreach (var item in _items)
        {
            if (item is EntryItem entry && entry.DeclaredHeight > max)
                max = entry.DeclaredHeight;
        }

        _maxDeclaredHeight = max;
    }

    private void EnsureSorted()
    {
        if (!_needsSort) return;

        _items.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        _needsSort = false;
    }

    // ── Render ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the toolbar at the top of the main viewport.
    /// Must be called inside an active ImGui frame.
    /// </summary>
    /// <param name="currentPerspective">
    /// The active perspective name used to filter perspective-bound entries.
    /// Pass an empty string (default) or the current perspective from
    /// <see cref="WindowManager.CurrentPerspective"/>.
    /// </param>
    /// <remarks>
    /// <b>BATCH-25:</b> This standalone-band method is kept for backward compatibility
    /// but is no longer called by <see cref="WindowManager"/>. The production path is
    /// now <see cref="RenderInline"/> (called inside the main menu bar).
    /// </remarks>
    public void Render(string currentPerspective = "")
    {
        EnsureSorted();

        float height = _maxDeclaredHeight + Gui.GetStyle().WindowPadding.Y * 2f;

        var viewport = Gui.GetMainViewport();
        // Anchor at the top of the work area (directly under the menu bar)
        Gui.SetNextWindowPos(viewport.WorkPos);
        Gui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, height));
        Gui.SetNextWindowViewport(viewport.ID);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove;

        // Dark background so the toolbar is visually distinct from window client areas.
        Gui.PushStyleColor(ImGuiCol.WindowBg, ToolbarBg);
        Gui.Begin("##MainToolbar", flags);
        Gui.PopStyleColor();

        RenderEntries(currentPerspective);

        Gui.End();
    }

    /// <summary>
    /// Renders the registered toolbar entries and separators inline — within the
    /// <b>current</b> ImGui window (no <c>Begin</c>/<c>End</c>, no <c>SetNextWindow*</c>).
    /// Must be called inside an active ImGui window such as the main menu bar
    /// (<see cref="ImGui.BeginMainMenuBar"/>).
    /// </summary>
    /// <param name="currentPerspective">
    /// The active perspective name used to filter perspective-bound entries.
    /// </param>
    /// <remarks>
    /// <b>BATCH-25:</b> This is the production render path. The toolbar now lives
    /// inside the main menu bar, to the right of the menus, eliminating the
    /// separate 64px band and recovering that vertical space.
    /// </remarks>
    public void RenderInline(string currentPerspective = "")
    {
        EnsureSorted();
        RenderEntries(currentPerspective);
    }

    /// <summary>
    /// Core render loop shared by <see cref="Render"/> (stand-alone band) and
    /// <see cref="RenderInline"/> (inline in the current window).  Iterates
    /// registered items in sort order, applies perspective filtering, and draws
    /// each entry or separator with <c>SameLine</c> between consecutive items.
    /// </summary>
    private void RenderEntries(string currentPerspective)
    {
        bool first = true;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];

            // Filter: global items (null perspective) always render;
            // perspective-bound items only render when the perspective matches.
            bool visible = item.Perspective == null || item.Perspective == currentPerspective;
            if (!visible) continue;

            if (!first)
                Gui.SameLine();

            first = false;

            if (item is EntryItem entry)
            {
                entry.RenderDelegate();
            }
            else if (item is SeparatorItem)
            {
                DrawSeparator();
            }
        }
    }

    /// <summary>
    /// Draws a vertical divider line spanning the full current frame height.
    /// Uses <c>GetFrameHeight()</c> at render time (not <c>_maxDeclaredHeight</c>
    /// which is captured at registration, before any per-frame style pushes take
    /// effect) so the line always fills the bar regardless of BATCH-26 padding.
    /// </summary>
    private static void DrawSeparator()
    {
        var cursor = Gui.GetCursorScreenPos();
        var drawList = Gui.GetWindowDrawList();
        float sepHeight = Gui.GetFrameHeight();
        uint sepColor = Gui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 1f));

        drawList.AddLine(
            cursor,
            cursor + new Vector2(0, sepHeight),
            sepColor);

        // Advance cursor past the separator (1 px wide + a small gap)
        Gui.Dummy(new Vector2(3, sepHeight));
    }
    // ── Observability (cgf==editor slice 2, item ⑤) ──────────────────────────

    /// <summary>⭐ The panel id every host publishes this singleton under.</summary>
    public const string PanelIdLiteral = "main_toolbar";

    /// <summary>⭐⭐ The KIND — identical on every host, so cross-host conformance groups by it.</summary>
    public const string PanelKindLiteral = "main-toolbar";

    /// <summary>
    /// ⭐⭐ Builds the view-model for the current item set. ⭐ Public so a rail can assert the model
    /// without an ImGui frame — ⛔ the draw path is not a testable seam.
    /// </summary>
    public MainToolbarPanelViewModel BuildViewModel(string currentPerspective)
    {
        EnsureSorted();

        var entries = new List<MainToolbarEntryView>(_items.Count);
        foreach (var item in _items)
        {
            entries.Add(new MainToolbarEntryView(
                Id:          item.Id,
                Kind:        item is SeparatorItem ? "separator" : "entry",
                SortOrder:   item.SortOrder,
                Perspective: item.Perspective,
                // ⭐ THE SAME predicate RenderEntries uses, three lines below — ⛔ not a re-derivation.
                Visible:     item.Perspective == null || item.Perspective == currentPerspective));
        }

        return new MainToolbarPanelViewModel(
            PanelIdLiteral, PanelKindLiteral, currentPerspective, entries);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Declares and registers the toolbar snapshot — and it is called even when the toolbar does
    /// NOT DRAW.</b>
    ///
    /// <para>🔴🔴 <b>That is the whole point, and it was measured.</b> 📐 <c>2026-08-25</c>: only
    /// <c>EditorSubsystem</c> registers main-toolbar entries, so in <c>--mode all</c> the manager holds
    /// zero items, <see cref="Height"/> is <c>0</c>, and <c>WindowManager</c>'s
    /// <c>if (_mainToolbar.Height &gt; 0f)</c> guard skips the render entirely. ⇒ ⛔ publishing from
    /// inside the draw would make *"this host offers no toolbar entries"* indistinguishable from
    /// *"this host's toolbar was never instrumented"* — and telling those two apart is exactly what §7 of
    /// the slice-2 design needs.</para>
    ///
    /// <para>⚠ <b>Stated plainly, because it bends <see cref="IPanelViewModel"/>'s usual invariant</b>
    /// *("the draw renders only from this")*: the toolbar's entries render through opaque host-supplied
    /// delegates, so this model never described pixels — it describes the <b>registered item set and its
    /// visibility</b>, which is well-defined whether or not a frame drew it. ⭐ The visibility predicate is
    /// still the SAME expression <c>RenderEntries</c> filters on.</para>
    /// </summary>
    public void PublishSnapshot(string currentPerspective)
    {
        PanelSnapshot.DeclareInstrumented(PanelIdLiteral);
        if (!PanelSnapshot.CaptureEnabled) return;

        PanelSnapshot.Register(BuildViewModel(currentPerspective));
    }

}
