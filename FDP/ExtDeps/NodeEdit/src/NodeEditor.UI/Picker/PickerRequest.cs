using System.Numerics;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Category tree node used by the Wide and Tree layouts to display a sidebar
/// hierarchy. Build from <see cref="PickerEntry.Category"/> strings if the
/// host does not supply an explicit tree.
/// </summary>
/// <param name="Name">Display label for this category node.</param>
/// <param name="Children">Child nodes, may be empty.</param>
public sealed record CategoryNode(string Name, IReadOnlyList<CategoryNode> Children);

/// <summary>
/// ⭐ CE-4142 (GL3) — a GROUP sidebar for the Grid layout (the thumbnail gallery): the groups to list in the request's own order, each with a
/// count, empty ones faded. Entries belong to a group by the first segment of <see cref="PickerEntry.Category"/>
/// (<c>"Ground vehicles/Tank"</c> = group + chip); the second segment is the chip row on top of the grid.
/// </summary>
/// <param name="Order">Group names, top to bottom.</param>
/// <param name="RecentGroup">The name of the VIRTUAL first group that holds no category: it lists the entries that are recent or starred
/// (G10); null ⇒ no such group.</param>
public sealed record PickerGroupSidebar(IReadOnlyList<string> Order, string? RecentGroup = null);

/// <summary>
/// Caller-supplied input to open a picker via <see cref="PickerRegistry.OpenPicker"/>.
/// Fully data-driven — the picker window renders items and emits the choice.
/// </summary>
public sealed class PickerRequest
{
    /// <summary>Stable key identifying this picker context (favorites + recent persistence).</summary>
    public required string ContextKey { get; init; }

    /// <summary>Window title shown to the user.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// <see cref="PickerEntry.Id"/> of the entry to pre-select when the picker opens — the host's default,
    /// so Enter/OK takes it without a click. Its category folders are expanded and it is scrolled into view.
    /// <see langword="null"/> (or an id not in the list, or a disabled entry) ⇒ no pre-selection.
    /// </summary>
    public string? InitialSelectionId { get; init; }

    /// <summary>Layout to use for rendering items.</summary>
    public PickerLayout Layout { get; init; } = PickerLayout.Standard;

    /// <summary>Selection mode (single / multi / multi-ordered).</summary>
    public PickerSelectionMode SelectionMode { get; init; } = PickerSelectionMode.Single;

    /// <summary>Source of items. Called once and cached on first open.</summary>
    public required Func<IEnumerable<PickerEntry>> ItemsProvider { get; init; }

    /// <summary>Initial search text (e.g. pre-filled from the dropped wire's type filter).</summary>
    public string InitialQuery { get; init; } = "";

    /// <summary>Screen position to anchor the window to. Null = centered in main viewport.</summary>
    public Vector2? AnchorScreen { get; init; }

    /// <summary>Optional category tree for Wide/Tree layouts. Null = built from Category strings.</summary>
    public CategoryNode? CategoryRoot { get; init; }

    /// <summary>Tree layout: merge a folder whose only content is one sub-folder into a single row
    /// ("Platform › Land › Tank"). Default off, so existing pickers are unchanged.</summary>
    public bool FoldSingleChildFolders { get; init; }

    /// <summary>Tree layout: show a preview pane under the tree for the focused entry — a large icon
    /// (its <see cref="PickerEntry.IconKey"/>), the name, the category and the description.</summary>
    public bool ShowPreview { get; init; }

    // ── CE-4168 (AQ108 D26) — five opt-in extensions; unset ⇒ the picker looks and behaves as before ──

    /// <summary>⭐ P1 — show a resizable pane to the RIGHT of the list/tree/grid for the focused entry (default content: its
    /// <see cref="PickerEntry.ImageKey"/> image scaled to the pane width, then name, category, description). Implied by <see cref="DrawPane"/>.
    /// Replaces the Tree's under-tree <see cref="ShowPreview"/> strip.</summary>
    public bool ShowSidePane { get; init; }

    /// <summary>⭐ CE-4141 — the window's size when it opens (applied once, <c>ImGuiCond.Appearing</c>); <see langword="null"/> ⇒ the layout's own rule. A source with a side pane asks for a wide window
    /// (the scenario picker: about 1000 × 640) so the preview picture is big enough to read.</summary>
    public System.Numerics.Vector2? DefaultSize { get; init; }

    /// <summary>⭐ P1 — initial side-pane width in pixels (a dragged width is remembered per <see cref="ContextKey"/>).</summary>
    public float SidePaneWidth { get; init; } = 280f;

    /// <summary>⭐ P1/P2 — draws the side pane's content for the focused entry (any ImGui widgets; their state lives in a host object —
    /// hand it back through <see cref="CapturePaneState"/>). Replaces the default content unless <see cref="DrawPaneAfterDefault"/>.</summary>
    public Action<PickerEntry, PickerPaneContext>? DrawPane { get; init; }

    /// <summary>⭐ P1 — draw the default content first and <see cref="DrawPane"/> below it, instead of replacing it.</summary>
    public bool DrawPaneAfterDefault { get; init; }

    /// <summary>⭐ P2 — called at confirm with the entry chosen (null if none); its result is <see cref="PickerResult.PaneState"/>.</summary>
    public Func<PickerEntry?, object?>? CapturePaneState { get; init; }

    /// <summary>⭐ P3 — footer buttons that REPLACE OK; the confirming one is returned in <see cref="PickerResult.ActionId"/>.
    /// Enter / double-click take the default action. Cancel stays.</summary>
    public IReadOnlyList<PickerAction>? Actions { get; init; }

    /// <summary>⭐ P4 — layouts the user may toggle between (e.g. Tree ⇄ Grid); the choice is remembered per <see cref="ContextKey"/>
    /// in the registry's <see cref="PickerViewStore"/>. Null/empty ⇒ no toggle. <see cref="Layout"/> is the first-use layout.</summary>
    public IReadOnlyList<PickerLayout>? LayoutChoices { get; init; }

    /// <summary>⭐ CE-4142 (GL3) — show the group sidebar (and chips, tile-size slider) in the Grid layout. Null ⇒ the classic Grid.</summary>
    public PickerGroupSidebar? GroupSidebar { get; init; }
}
