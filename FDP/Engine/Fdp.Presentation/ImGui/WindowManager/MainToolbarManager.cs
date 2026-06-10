using System.Numerics;
using ImGuiNET;

namespace Fdp.Presentation.WindowManager;

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
    /// Exposed for testability. Returns the ordered list of visible items
    /// (entries and separators) for a given perspective, without performing
    /// any ImGui draw calls. This is the headless-test seam.
    /// </summary>
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

        Gui.End();
    }

    /// <summary>
    /// Draws a vertical divider line over the declared toolbar height.
    /// </summary>
    private void DrawSeparator()
    {
        var cursor = Gui.GetCursorScreenPos();
        var drawList = Gui.GetWindowDrawList();
        float sepHeight = _maxDeclaredHeight;
        uint sepColor = Gui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 1f));

        drawList.AddLine(
            cursor + new Vector2(0, 0),
            cursor + new Vector2(0, sepHeight),
            sepColor);

        // Advance cursor past the separator (1 px wide + gap)
        Gui.Dummy(new Vector2(1, sepHeight));
    }
}
