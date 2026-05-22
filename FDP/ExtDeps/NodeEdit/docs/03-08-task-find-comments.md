# Tasks T-17 and T-18 — Find Bar and Comments/Reroutes

---

# T-17 — UI: Find Bar and Find-in-Asset Panel

## Goal
Implement the slim "find bar" inserted at the top of the canvas (Ctrl+F)
and the broader find-results panel for asset-wide search (Ctrl+Shift+F).

## Project
`NodeEditor.UI`

## References

**Specs:**
- `../specs/D1-to-D4-flows.md` §D.4 (Find / Go-to UX) — full normative
- `../instructions/01-spec-brief-part2.md` §21 (Find / navigation)

**Kernel:**
- `../kernel/03-search-spatial-constants.md` — `FuzzyMatcher`
- `../kernel/04-my-blueprint-and-rest.md` — `IGraphSearchProvider`

## Deliverables

```
src/NodeEditor.UI/
    Find/
        FindBar.cs                  // slim search bar atop canvas
        FindResultsPanel.cs         // side panel for asset/project results
        FindEngine.cs               // pure logic: query parsing + ranking
        FindQuery.cs                // parsed query (text + prefixes)
        FindResult.cs               // one match (node, pin, comment, …)
        FindScope.cs                // CurrentGraph / Asset / OpenTabs / WholeProject
```

## Public surface

```csharp
namespace NodeEditor.UI.Find;

/// <summary>
/// The slim find bar inserted above the canvas (Ctrl+F).
/// Owns its own visibility, search text, and active-match index.
/// On query change, asks the <see cref="FindEngine"/> to enumerate
/// matches in the current graph and exposes them as a navigable list.
/// </summary>
public sealed class FindBar
{
    public FindBar(GraphView view, FindEngine engine);

    /// <summary>Show/hide the bar. Ctrl+F sets true; Esc on empty bar sets false.</summary>
    public bool IsVisible { get; set; }

    /// <summary>Active scope.</summary>
    public FindScope Scope { get; set; } = FindScope.CurrentGraph;

    /// <summary>Case-sensitive search.</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>Regex mode.</summary>
    public bool RegexMode { get; set; }

    /// <summary>Current match index within <see cref="Results"/>.</summary>
    public int ActiveIndex { get; private set; }

    /// <summary>All matches in the current graph.</summary>
    public IReadOnlyList<FindResult> Results { get; private set; } = Array.Empty<FindResult>();

    /// <summary>Draw the bar inside the current ImGui region (above the canvas).</summary>
    public void Draw();

    /// <summary>Advance / retreat the active match (F3 / Shift+F3).</summary>
    public void Next();
    public void Previous();
}
```

## Bar layout

```
🔍 [search text...........] ▶ ▲ ▼  [Aa] [.*]  3/12  ✕
   │                          │ │ │   │    │     │     │
   │                          │ │ │   │    │     │     close
   │                          │ │ │   │    │     match count
   │                          │ │ │   │    regex toggle
   │                          │ │ │   case-sensitive toggle
   │                          │ │ next match (F3)
   │                          │ previous match (Shift+F3)
   │                          scope dropdown
   live search text
```

ImGui IDs to use:
- Search input: `##find-search`
- Scope dropdown: `##find-scope`
- Buttons: `##find-prev`, `##find-next`, `##find-case`, `##find-regex`, `##find-close`

## Query parsing

`FindEngine` parses prefixes from the query string:

```
type:Vector3      → filter by pin type
kind:branch       → kind name substring
category:math     → category match
var:Health        → variable references
func:Compute      → function call sites
error:            → nodes with errors
warning:          → nodes with warnings
breakpoint:       → nodes with breakpoints
watched:          → nodes with watched pins
```

After prefix extraction, the remaining text becomes the "free text" matched
against searchable text via `FuzzyMatcher` (when not regex) or
`Regex.IsMatch` (when regex mode).

```csharp
public sealed record FindQuery(
    string FreeText,
    IReadOnlyDictionary<string, string> Prefixes);

public static class FindQueryParser
{
    public static FindQuery Parse(string raw);
}
```

## FindResult

```csharp
namespace NodeEditor.UI.Find;

public sealed record FindResult(
    FindResultKind Kind,
    GraphId? Graph,
    NodeId? Node,
    PinId? Pin,
    CommentId? Comment,
    string DisplayLabel,
    string MatchSnippet,
    IReadOnlyList<int> MatchPositions);

public enum FindResultKind { Node, Pin, PinDefault, Comment, Variable, Function }
```

## Searchable text per node

Default searchable text per node = title + subtitle + category + pin labels
+ pin default values (stringified) + comment text + variable references.

If `IGraphSearchProvider` is registered on host services, the engine
additionally calls `provider.GetSearchableText(node)` and appends.

```csharp
public sealed class FindEngine
{
    public FindEngine(IGraphModel model, IGraphSearchProvider? extras);

    public IEnumerable<FindResult> Search(FindQuery query, FindScope scope, GraphView view);
}
```

## Visualization in canvas

When `FindBar.IsVisible` and `Results.Count > 0`:
- Matching nodes get yellow outline (added to `Canvas` via a new render
  pass that reads `findBar.Results`).
- Active match (index = `ActiveIndex`) gets stronger highlight + pulse.
- Non-matching nodes dim to ~40% alpha.
- Canvas auto-centers on active match (animated 180 ms ease-out-cubic) on
  index change.

Add an overload to `CanvasRenderer.Render` (T-12) that accepts a
`FindBar` reference and renders the find overlay if visible. The find bar
itself draws above the canvas region (ImGui horizontal layout).

## Esc behavior

- Has text: clear text, stay visible.
- Empty: close bar.

## FindResultsPanel (asset scope)

When user invokes Ctrl+Shift+F or selects "Current Asset" scope:
- Side panel opens (separate ImGui window, host-positioned).
- Groups results by graph, collapsible.
- Click result → opens graph tab (via callback), frames the node, sets
  active match.

```csharp
public sealed class FindResultsPanel
{
    public FindResultsPanel(Action<GraphId, NodeId?> navigateTo);
    public IReadOnlyList<FindResult> Results { get; set; } = Array.Empty<FindResult>();
    public bool IsVisible { get; set; }
    public void Draw();
}
```

## Acceptance

- Ctrl+F opens the bar; typing live-filters.
- F3 cycles next match; Shift+F3 previous; wrap at ends.
- Esc with text clears; Esc on empty closes.
- Matching nodes get yellow outline.
- Search prefixes `kind:` and `category:` work.
- Demo (T-20) wires up the bar and the panel.

## Estimated Size
~300 LOC.

## Status
Pending.

---

# T-18 — UI: Comments and Reroutes

## Goal
Implement comment-box rendering, drag/resize/rename interactions, and
reroute waypoint rendering and manipulation.

## Project
`NodeEditor.UI`

## References

**Specs:**
- `../specs/D8-comments-reroutes.md` — full normative behavior
- `../instructions/01-spec-brief-part2.md` §22 (comments & reroutes)

## Deliverables

```
src/NodeEditor.UI/
    Canvas/
        CommentRenderer.cs           // draw + hit-test comment boxes
        ReroutesRenderer.cs          // draw reroute waypoints, hit-test
        CommentInteractions.cs       // drag, resize, rename state machine
        ReroutesInteractions.cs      // drag, double-click delete, insert
```

These are sub-components of the canvas renderer (T-12). They register with
the main `CanvasRenderer` so paint order works:

1. Background grid (T-12 GridRenderer)
2. **Comment bodies** ← CommentRenderer (this task)
3. Wires (T-12 WireRenderer)
4. Nodes (T-12 NodeRenderer)
5. **Comment selection outlines** ← CommentRenderer (this task)
6. **Reroute glyphs** ← ReroutesRenderer (this task)
7. UI overlays (drag-wire ghost, marquee)

Update `CanvasRenderer.Render` (T-12) to insert these phases at the right
positions if not already.

## Comment rendering

```csharp
namespace NodeEditor.UI.Canvas;

internal static class CommentRenderer
{
    /// <summary>Draw a comment's body and header. Returns the rect.</summary>
    public static RectF Render(
        ICommentModel comment,
        ViewportState viewport,
        bool selected,
        IEditorTheme theme);

    /// <summary>Draw selection outline (called from later render phase).</summary>
    public static void RenderSelectionOutline(
        ICommentModel comment,
        ViewportState viewport,
        IEditorTheme theme,
        bool isPrimary);
}
```

Visual details:
- Header strip height = `theme.NodeHeaderHeight` (~24 px at zoom 1.0).
- Header alpha = full; body alpha = 0.20.
- 1-px border at full alpha.
- 4-px corner radius.
- Title text inside header, vertically centered.
- 8 resize handles on selection (4 corners + 4 edge midpoints).

## Hit-test

```csharp
internal static class CommentHitTest
{
    public static CommentHitResult HitTest(
        ICommentModel comment,
        Vector2 cursorCanvas);
}

public readonly record struct CommentHitResult(
    bool Hit,
    CommentHitZone Zone,
    int? ResizeHandleIndex);    // 0-7 if Zone == ResizeHandle

public enum CommentHitZone { None, Header, Body, ResizeHandle }
```

Hit priority: ResizeHandle > Header > Body. Body hits return Hit=true but
are treated as click-through by the canvas (it then tries the next phase).

## Drag interactions

When user LMB-down on comment header:
- Set `InteractionState.Mode = DraggingComment`.
- Snapshot enclosed nodes (`comment.MoveWithContents` true):
  ```csharp
  var enclosed = view.Model.Nodes
      .Where(n => commentRect.FullyContains(nodeBounds(n)))
      .Select(n => n.Id)
      .ToHashSet();
  view.Interaction.CommentDragContents = enclosed;
  ```

Per-frame during drag:
- Update `view.Interaction.DragOverridePositions[comment.Id] = newPos` (a
  separate dict from node overrides; or reuse same dict — see decision
  below).
- For each enclosed node: also update `DragOverridePositions[n] = origPos + delta`.

On LMB-up:
- Build a Batch:
  - `UpdateComment(commentId, position)` for the moved comment.
  - `MoveNodes` for the enclosed nodes' final positions.
- Dispatch via `view.Execute(batch)`.

Modifier behavior:
- Shift held: drag comment alone (no contents). Don't populate
  `CommentDragContents`.
- Alt held: move only contents (comment stays). Snapshot contents at drag
  start; comment position not modified.

### Design choice to log

The `InteractionState.DragOverridePositions` dict currently maps `NodeId →
Vector2`. Comments need their own override; either:
- Add `CommentDragOverridePositions: Dictionary<CommentId, Vector2>` to
  `InteractionState`, or
- Use a unified entity-id override dict.

**Decision:** add a separate `CommentDragOverridePositions` field to
`InteractionState`. Keeps types clean.

Update `InteractionState.cs` (T-10) to add:
```csharp
public Dictionary<CommentId, Vector2> CommentDragOverridePositions { get; } = new();
```

Reset to empty in `ResetToIdle()`.

## Resize interactions

LMB-down on a resize handle:
- `Mode = ResizingComment`.
- Snapshot initial size + position + handle index.

Per-frame: compute new size based on which handle (corner = both
dimensions, edge = one dimension) and cursor delta.

LMB-up: dispatch `UpdateComment(commentId, position, size)`.

## Inline rename

LMB double-click on comment header:
- Switch comment header to text-edit mode.
- Show `InputTextMultiline` overlaid on header strip.
- Enter / focus-out commits via `UpdateComment(Text)`.
- Esc cancels.

State tracked in InteractionState:
```csharp
public CommentId? RenamingComment { get; set; }
```

Add to `InteractionState`. Reset in `ResetToIdle()`.

## Reroute rendering

```csharp
namespace NodeEditor.UI.Canvas;

internal static class ReroutesRenderer
{
    /// <summary>Draw all reroute waypoints for all links. Called after wires + nodes.</summary>
    public static void Render(
        IGraphModel model,
        SelectionState selection,
        ViewportState viewport,
        IEditorTheme theme);

    /// <summary>Hit-test reroutes; returns first hit (in spec hit priority).</summary>
    public static RerouteRef? HitTest(
        IGraphModel model,
        Vector2 cursorCanvas,
        float radiusPx);
}
```

Visual details:
- Diameter ~12 px at zoom 1.0 (scaled with zoom).
- Color: wire's color (= source pin's color, computed by `WireRenderer` from
  `TypeSystem.GetPinColor`).
- Outline ~1.5 px darker.
- Selected: 2-px theme accent outline overlay.
- Hover: scaled to ~1.2×.

## Reroute interactions

- **LMB click:** select (with Shift/Ctrl modifiers per standard selection
  rules).
- **LMB drag:** `Mode = DraggingReroutes`. Per frame: update
  `view.Interaction.RerouteDragOverridePositions[rerouteRef] = newPos`.
  Both wire segments follow. On LMB-up: dispatch `MoveReroute(linkId,
  waypointIndex, newPos)`.
- **LMB double-click:** dispatch `RemoveReroute(linkId, waypointIndex)`.
- **Alt+click:** same as double-click.
- **RMB:** context menu (Delete, Cut/Copy/Duplicate).

### Add to InteractionState

```csharp
public Dictionary<RerouteRef, Vector2> RerouteDragOverridePositions { get; } = new();
```

## Reroute creation

- **Double-click on wire:** dispatch `InsertReroute(linkId, position)`.
  Position-along-wire calculation:
  ```csharp
  // For each bezier segment, sample N=24 points. Find the segment containing
  // the click point (closest sample point). The new waypoint is inserted
  // at the segment's index (or 0 if before first existing waypoint).
  ```
- **RMB on wire → "Insert Reroute Node Here":** same as above.
- **Wire drag → drop on empty canvas:** picker offers "+ Add Reroute" at
  top of results. Selection triggers `InsertReroute` followed by re-link.
  (Picker integration handled by canvas event hook from T-14.)

## Acceptance

- Demo (T-20) includes a comment box around a few nodes; drag header moves
  contents; resize handles work; double-click header renames.
- Click body of comment passes through (selects underlying node).
- Wire with reroute renders correctly; reroute drag moves it; double-click
  removes it.
- Comment Z-order respected (later draws on top).

## Estimated Size
~350 LOC across all sub-files.

## Status
Pending.
