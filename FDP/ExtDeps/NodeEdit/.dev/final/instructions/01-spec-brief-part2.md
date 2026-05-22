# 01 — Spec Brief (Part 2)

Continuation of `01-spec-brief.md`. Sections 15–29.

## 15. Search popup (node creation)

Opens on: `Tab` / `Space` on empty canvas, RMB → Add Node, wire drag released
on empty canvas, double-click on empty canvas.

This is a *specialization of the generic picker* (§16) wired to
`INodeCatalog`. Same UX, specific source. Always use the picker
implementation; do not write a separate popup.

When opened from a wire drag:
- The picker is filtered by `INodeCatalog.QueryForPinContext`.
- "Context-aware" toggle is ON by default.
- The selected node is placed at cursor canvas position AND auto-connected
  via a Batch command (AddNode + AddLink).

When opened from Tab/RMB:
- Filtered by `INodeCatalog.Query` (all nodes by default).
- "Context-aware" toggle is OFF.
- Selected node placed at cursor position; no auto-connection.

## 16. Generic picker

### Window characteristics

- **Floating window**, NOT an ImGui popup. Uses
  `ImGui.Begin(name, &open, flags)` with appropriate flags
  (`NoCollapse | NoSavedSettings | AlwaysAutoResize=false`).
- Title bar with close (×) button.
- Borderless? No — show borders for clear definition.
- **Always-on-top** while open.
- Default size: 520 × 520 px. Resizable. Position remembered per source key.
- **Position clamping:** ensure it's fully on-screen at open time.

### Lifecycle

- Opens via `IPicker.Open<T>(source, screenPos, onPick, onCancel)`.
- Closes on:
  - Selection (Enter / double-click) → calls `onPick`.
  - Esc → calls `onCancel`.
  - Click outside the picker → calls `onCancel` (treated as Esc).
- **Soft-modal**: floats over editor; underlying animations continue.
- Only one picker open at a time. Opening a new one closes the previous.

### Layout — Standard mode

```
┌────────────────────────────────────────────────────┐
│ Title                                          ✕   │  ← title bar
├────────────────────────────────────────────────────┤
│ 🔍 [ search                ]  [☐ Context-aware]   │  ← search row
├────────────────────────────────┬───────────────────┤
│ list (virtualized)             │ preview pane      │
│ ★ Favorites                    │ (driven by item)  │
│ ⌚ Recent                      │                   │
│ ▾ Category 1                   │                   │
│     • Item                     │                   │
│     • Item                     │                   │
│ ▸ Category 2 (collapsed)       │                   │
├────────────────────────────────┴───────────────────┤
│ ↑↓ Navigate  ⏎ Select  Esc Cancel  Ctrl+Space Ctx │  ← footer hints
└────────────────────────────────────────────────────┘
```

### Layout variants

```
Standard:  search top, list left (60%), preview right (40%).
Compact:   search top, list fills (no preview pane); ~320 × 360.
Wide:      search top, list+preview wider; ~800 × 600.
Grid:      tiled thumbnails with labels.
Tree:      indented hierarchy as the primary axis.
```

Source declares preferred layout via `IPickerSource<T>.PreferredLayout`.

### Source interface

```csharp
public interface IPickerSource<TItem>
{
    string Title { get; }
    string EmptyResultText { get; }
    PickerLayout PreferredLayout { get; }
    PickerSelectionMode SelectionMode { get; }
    QueryCost Cost { get; }
    bool IsAsync { get; }
    bool AllowsDragOut { get; }
    bool AllowsDragIn { get; }

    IReadOnlyList<TItem> Query(string text, IReadOnlyDictionary<string, object?>? context);
    Task<IReadOnlyList<TItem>> QueryAsync(string text, IReadOnlyDictionary<string, object?>? context, CancellationToken ct);

    void RenderItem(TItem item, bool selected, bool keyboardFocused, IPickerRenderContext ctx);
    void RenderPreview(TItem item, IPickerRenderContext ctx);
    bool IsPreviewExpensive(TItem item);

    string GetSearchableText(TItem item);
    string GetItemKey(TItem item);
    bool CanAcceptDrop(object payload);
}
```

### Keyboard shortcuts inside picker

| Key | Action |
|---|---|
| ↑ / ↓ | Navigate items |
| PgUp / PgDn | Navigate by page |
| Home / End | First / last result |
| Enter | Select highlighted |
| Esc | Cancel |
| Tab | If single match: autocomplete. Else cycle focus. |
| Shift+Tab | Reverse cycle |
| Ctrl+Space / Ctrl+F | Toggle Context-aware |
| Ctrl+↑ / Ctrl+↓ | Jump between section headers |
| Alt+↑ / Alt+↓ | Jump skipping section headers |
| Space | (multi-select only) Toggle |
| Ctrl+Enter | (multi-select only) Accept selection |
| F1 / ? | Show help overlay |
| Ctrl+Backspace | Delete word-back in search |
| Ctrl+A | Select all text in search |
| Insert | Pin/unpin highlighted as favorite |

### Mouse interactions

- Single click row: highlights (focus moves).
- Double click row: selects.
- Click section header: collapse/expand.
- Click ☆/★ icon: toggle favorite.
- Click outside window: cancel.
- Wheel: scroll.

### Search behavior

- Live filtering on every keystroke.
- Debounce 0/60/120 ms based on `QueryCost`.
- For async sources, dim previous results during in-flight query; cancel
  previous via `CancellationToken` when a new one starts.
- Fuzzy matcher: see `kernel/FuzzyMatcher.cs`. Tiered ranking:
  1. Exact display name (10000)
  2. Display name prefix (5000 + bonus)
  3. Word-start match (3000)
  4. CamelCase boundary (2500)
  5. Substring in name (1500)
  6. Substring in keywords (1000)
  7. Fuzzy char-order (500 + bonus)
- Boosts: recently used (+500 or +1000), favorites (+2000).
- Matched character positions returned for highlighting.

### Special prefixes

- `> category` — search by category only
- `: type` — search by pin type
- `# keyword` — search keywords only
- `?` — show help

### Sections in the result list (fixed order)

1. ★ Favorites (if any match query)
2. ⌚ Recent (top 5 most recently used)
3. Category sections (host hierarchy)
4. Deprecated / archived (collapsed by default)
5. Incompatible (only when Context-aware OFF)

### Empty state

Shown when no results:
```
✦ No results for "xyz"

Try:
• Check spelling
• Toggle "Context-aware" off
• Search by category: > math

─────
Recent picks:
• Item 1
• Item 2
```

### Multi-select mode

- Each row has a checkbox.
- Header shows "N of M selected" + Clear button.
- Footer shifts hints (Space toggle, Ctrl+Enter accept).
- Shift+↑↓ extends selection.

### Drag in / out

- **Drag in:** if source accepts the dropped payload type, on drop selects
  the item immediately and closes.
- **Drag out:** drag a row out of the picker; on release outside the
  picker, closes the picker and `onPick` is called with the dragged item.
  Useful for placing a node by drag onto canvas.

### Favorites & recents

- Persisted globally (per-user, per-source-key) under editor session state.
- Favorites limit: 50 per source.
- Recents limit: 20 per source.
- Sort recents by last-used desc; user can clear via right-click on section.

### Error handling

| Situation | Behavior |
|---|---|
| Source throws | Show error row "⚠ Query failed: {msg}"; log; keep picker open. |
| Item render throws | Catch per-item; show placeholder "⚠ {key}". |
| Preview throws | "⚠ Preview unavailable". |
| Source returns >1000 items | Virtualization handles; footer: "showing top 1000 of 10000". |

## 17. Mini-editor catalog

Built-in `IPinDefaultValueEditor` implementations registered to standard
TypeKeys. The host can override any by registering its own.

### Standard editors (must ship)

| Type | TypeKey | Editor |
|---|---|---|
| bool | `System.Boolean` | Checkbox |
| int | `System.Int32` | DragInt |
| long | `System.Int64` | DragInt (wide) |
| float | `System.Single` | DragFloat |
| double | `System.Double` | DragFloat |
| string | `System.String` | InputText (single-line) |
| Vector2 | `System.Numerics.Vector2` | Two DragFloats |
| Vector3 | `System.Numerics.Vector3` | Three DragFloats |
| Vector4 | `System.Numerics.Vector4` | Four DragFloats |
| Quaternion | `System.Numerics.Quaternion` | Y/P/R degrees (3 DragFloats) |
| Color (Vec4) | `NodeEditor.Color` | Color swatch button |
| Guid | `System.Guid` | Truncated button → picker |
| Enum (small) | depends | ImGui combo |
| Enum (large or [Flags]) | depends | Button → picker |

### DragFloat / DragInt specifics

- Hover: cursor → horizontal ↔.
- LMB drag: continuous change, speed `step * |dx|`.
- Modifiers: Ctrl ×10, Shift ÷10, Alt ÷100.
- Single click (no drag): enters text-edit mode, all selected.
- Text-edit accepts expressions (see expression evaluator below).
- Tab moves to next pin's editor on same node.
- Right-click → menu: Reset to Default, Copy, Paste, Snap to Step, Set to Min/Max.
- During drag: commit deferred (one undo step per drag gesture).
- Suffix from `Metadata.Units`: `[ 3.14 m ]`.

### Multi-field editors (Vector*)

- Tight horizontal layout: `X[ 0.000 ] Y[ 0.000 ] Z[ 0.000 ]`.
- Drag on `X` / `Y` / `Z` label drags that component (extra hit area).
- Tab between components.
- Right-click → Copy XYZ, Paste XYZ, Set All To…, Normalize (V3 only).
- Below MaxWidth threshold: collapse to button `[ XYZ ▾ ]` opening popup.

### Color editor

- Swatch ~24×16. Click opens ImGui `ColorPicker4` in popup.
- Popup also shows: hex input, named-color dropdown, recent-colors strip.
- Right-click swatch: Copy hex, Paste hex, Reset, Set alpha to 1.

### Asset/Entity reference editors

- Button shows current value's display name (truncated).
- Click opens generic picker.
- Right-click: Find in Content Browser, Clear, Copy/Paste Reference.
- Drop target: highlights green on drag-hover of compatible payload.

### Struct pin editors

Two modes:
- **Composite (default):** single pin, inline button `[ Transform ▸ ]` opens
  popup with sub-field editors.
- **Split:** struct pin replaced by N sub-pins.

Toggle via RMB → Split Struct Pin / Recombine Struct Pin.

### Array editors

Button `[ [N items] ▸ ]` opens popup:
```
Array<float> [3]
[0]  [ 1.5 ]      [×]
[1]  [ 2.0 ]      [×]
[2]  [ 3.5 ]      [×]
[ + Add ]
```

- Element editor recursively dispatched.
- Reorder via grip handle (V2).
- RMB header: Clear, Sort, Paste from JSON.

### Expression evaluator

Used by drag-float/int when in text-edit mode. Whitelist:
- Operators: `+ - * / % ^ ( )`
- Constants: `pi tau e`
- Functions: `sin cos tan asin acos atan sqrt abs floor ceil round min max clamp deg rad`
- Suffixes: `deg` (×π/180), `rad`
- Scientific: `1.5e-3`

On parse error: field shakes ~150ms, red tooltip 2s, reverts to previous value.

## 18. My Blueprint panel

Left-side panel (default). Tree-view organized by section:

```
Graphs               (host-defined entries)
Functions            (+ add button)
Macros               (+ add button)
Custom Events        (+ add button)
Variables            (+ add button)
Event Dispatchers    (+ add button)
Interfaces           (V2)
```

### Host interface

```csharp
public interface IMyBlueprintModel
{
    IReadOnlyList<MyBlueprintSectionDescriptor> Sections { get; }
    IReadOnlyList<MyBlueprintItem> GetItems(string sectionId);
    event Action? Changed;
}
```

Full record definitions in `kernel/Interfaces.MyBlueprint.cs`.

### Item visuals

```
Variables:    🟦 ▣ Health : float        (blue dot = type color)
              🟥 ▣ Damage : float    [exposed]
Functions:    ƒ ComputeDamage                  (blue header conceptually)
              ƒ IsAlive               [pure]   (green badge)
Macros:       ⚙ ForEachWithBreak             (orange-ish)
Events:       ⚡ OnEnemyKilled                 (red badge)
Dispatchers:  📢 OnHealthChanged
```

Type color comes from `ITypeSystem.GetPinColor(varType)` — matches the
wire color so user has visual continuity.

### Categories

Nested categories within a section. User-defined via item's CategoryPath
field (`"Combat/Stats"`). Items without category live in virtual
"default" folder. Categories sort alphabetically; items within a category
sort alphabetically.

### Search box

Top of panel. Same fuzzy matcher as picker (§16). Categories auto-expand
to reveal matches. Esc / clear restores collapse state.

### Interactions

- **LMB click item:** selects, updates Details panel target. No navigation.
- **Double-click item:** navigates (opens graph, focuses variable, etc.).
- **RMB click:** context menu per item type.
- **Drag onto canvas:**
  - Variable → pops menu Get/Set (Ctrl=Get, Alt=Set).
  - Function/Macro/CustomEvent → places call node.
  - Drop on wire → insert if compatible.
- **Drag within section to reorder:** V2.

### Section-specific context menus

See spec file `D.6-my-blueprint-panel.md` for full menu contents.

### Keyboard navigation when panel focused

| Key | Action |
|---|---|
| ↑ ↓ | Move selection |
| ← → | Collapse / expand folder |
| Home / End | First / last visible |
| Enter | Navigate (= double-click) |
| F2 | Rename |
| Delete | Delete |
| Ctrl+D | Duplicate |
| F4 | Open Properties |
| / | Focus search box |

### Footer

Two toggles: "Show Inherited" (V2), "Show Generated" (V2). Both default off.

### Multi-selection

**Not supported in MVP.** Single-select only.

## 19. Details panel

Right-side panel (default). Stateless dispatcher: shows properties of
currently-selected entity.

### Target types

```csharp
public abstract record DetailsTarget
{
    public sealed record None : DetailsTarget;
    public sealed record SingleNode(NodeId Id) : DetailsTarget;
    public sealed record MultipleNodes(IReadOnlyList<NodeId> Ids) : DetailsTarget;
    public sealed record Variable(string VariableId) : DetailsTarget;
    public sealed record Function(string FunctionId) : DetailsTarget;
    public sealed record Macro(string MacroId) : DetailsTarget;
    public sealed record CustomEvent(string EventId) : DetailsTarget;
    public sealed record EventDispatcher(string DispatcherId) : DetailsTarget;
    public sealed record LocalVariable(string FunctionId, string LocalId) : DetailsTarget;
    public sealed record FunctionEntry(string FunctionId) : DetailsTarget;
    public sealed record Comment(CommentId Id) : DetailsTarget;
    public sealed record Asset : DetailsTarget;
}
```

Target priority (highest wins):
1. Canvas selection (primary).
2. My Blueprint selection.
3. `Asset` (when nothing selected).

### View provider

```csharp
public interface IDetailsViewProvider
{
    bool CanHandle(DetailsTarget target);
    IDetailsView Build(DetailsTarget target, IDetailsContext ctx);
}

public interface IDetailsView
{
    void Draw(IDetailsRenderContext ctx);
    bool IsDirty { get; }
    void Commit();
    void Revert();
}
```

Multiple providers can register; first matching `CanHandle` wins. Default
fallback: read-only property tree via reflection.

### Layout

```
┌────────────────────────────────────┐
│ Details                       ⋮    │
├────────────────────────────────────┤
│ Breadcrumb / target label          │
├────────────────────────────────────┤
│ 🔍 [filter properties]   (V2)      │
├────────────────────────────────────┤
│ ▾ Section 1                        │
│     Label: [editor]                │
│     Label: [editor]                │
│ ▾ Section 2                        │
│     ...                            │
└────────────────────────────────────┘
```

- Two-column inside sections (label ~140 px, control fills remaining).
- Sections collapsible; collapse state persists **globally** per section name
  (not per-target).
- Min width 280 px; default 360 px.

### Property change flow

All mutations go through commands (§5). View accumulates pending changes;
on commit (mouse-up, Enter, focus-out) emits commands. Never directly
mutates host model.

### Multi-select target

Shows intersection of properties; mixed values shown as `[ ─ ]` placeholder;
setting a mixed field applies to all.

### Overflow menu (⋮)

- Collapse All Sections
- Expand All Sections
- Reset This Item to Defaults
- Help…
- Show Help Tooltips: ☑

## 20. Authoring flows

### 20.1 Custom event creation

`+ Custom Event` opens form:
```
Name:        [               ]
Category:    [ ▾ ]
Description: [               ]
Parameters:
    + Add parameter
Replicated:  ☐
Reliable:    ☐ (greyed unless Replicated)
[Cancel] [Create]
```

On create:
- New custom event added.
- New graph tab opens.
- Entry node placed at center of canvas.
- Focus moves to new graph.

Entry node:
- Red header (Event category).
- No input exec pin; one output exec pin.
- Data outputs match parameter list.
- Cannot be deleted from within graph.
- Renaming via inline double-click on header OR Details panel.

Parameters edited in Details panel when entry node selected. Adding/removing/
reordering parameters propagates to all call sites (preserves wires by
parameter Guid).

Call node: drag custom event from My Blueprint → canvas. Purple header
(custom event call).

### 20.2 Function creation

`+ Function` opens form similar to custom event, with:
- Inputs and Outputs (separate lists).
- "Pure" checkbox.

On create: graph opens with Entry node (left, blue header) and Return node
(right). Function graph can have multiple Return nodes (Unreal-style).

Entry and Return nodes are non-deletable in their function graph.

Pure functions: no exec pins on Entry/Return.

Local variables: scoped to function. Listed in My Blueprint under the
function as a sub-section.

### Collapse to Function

`Ctrl+E` on selection:
1. Detect inputs (wires entering selection) and outputs (wires exiting).
2. Open creation form pre-populated.
3. On confirm:
   - Create new function with detected signature.
   - Move selected nodes into function graph.
   - Replace selection with a call node.
   - Reconnect external wires.
4. Single Batch command, single undo step.

Edge cases:
- Multiple exec exits → multiple output exec pins.
- Pure subgraph → offered as Pure.
- Disconnected components → rejected.

### 20.3 Macro creation

`+ Macro` opens form. No Pure option. Inputs/outputs can be exec OR data.
Wildcards allowed.

Macro Entry has output exec pins; Macro Outputs has input exec pins.
Multi-exec topology supported (unlike functions).

Latent nodes allowed inside macros (forbidden in functions).

Call node visual shows resolved wildcard types: `ForEachWithBreak <Vector3>`.

RMB call node → Expand Node inlines the macro body.

### Collapse to Macro

Same as Collapse to Function but produces a macro. UI offers choice:
- Function
- Macro
- Auto (chooses based on selection: latent → must be macro;
  multi-exec → macro; pure data → pure function; default → function)

## 21. Find / navigation

### Find in Graph (Ctrl+F)

Slim find bar at top of canvas:
```
🔍 [search] ▶ ▲ ▼ [Aa] [.*]  3/12  ✕
```

- Live filter as you type.
- Matching nodes get yellow outline.
- Active match gets stronger highlight + pulse.
- Non-matching nodes dim to ~40% opacity.
- Canvas auto-centers on active match (animated 180ms).
- F3 / Shift+F3 next/prev.
- Esc: clear if has text, close if empty.

### Find in Asset (Ctrl+Shift+F)

Same bar but scope = Asset. Side panel shows results grouped by graph:
```
"Multiply" — 12 matches across 4 graphs
▾ EventGraph (5)
    Multiply (Vector × Vector)
    Pin 'A' = Multiplier
▾ ComputeDamage (3)
    ...
```

Click result → opens graph, frames node.

### Searchable text

Per node: title, subtitle, category, pin labels, pin defaults
(stringified), comment text, variable references.

Host extends via `IGraphSearchProvider.GetSearchableText(node)`.

### Scope dropdown ▶

- ◉ Current Graph
- ○ Current Asset (all graphs)
- ○ Open Tabs
- ○ Whole Project (host-provided)

### Search prefixes

| Prefix | Searches |
|---|---|
| `type:Vector3` | Nodes with that pin type |
| `kind:branch` | Nodes whose Kind contains substring |
| `category:math` | Nodes in category |
| `var:HealthCurrent` | References to variable |
| `func:ComputeDamage` | Call sites to function |
| `error:` | Nodes with errors |
| `warning:` | Nodes with warnings |
| `breakpoint:` | Nodes with breakpoints |
| `watched:` | Nodes with watched pins |

### Find References (Ctrl+Shift+F on item)

Opens Find Results panel in Asset scope, pre-filled with structured query.

### Go to Definition (F12)

- Function call → function's graph, centered on Entry.
- Variable reference → My Blueprint, scrolls to variable.
- Custom event call → event's graph.
- Macro call → macro's graph (or expand inline if configured).

## 22. Comments & reroutes

### Comments — data model

```csharp
public sealed record CommentBox(
    CommentId Id,
    string Text,
    Vector2 Position,
    Vector2 Size,
    Vector4 Color,
    int ZOrder,
    bool MoveWithContents);
```

### Comments — visual

- Header strip: ~24 px tall, full alpha of `Color`.
- Body: same `Color` at ~20% alpha.
- 1-px border at full alpha.
- Resize handles when selected: 4 corners + 4 edge midpoints.
- Selection outline: 2-px theme accent over everything.
- Rendered at paint step 2 (behind wires and nodes; see §6 paint order).

### Comments — creation

- Path 1: Select nodes, press `C` → comment created around selection.
- Path 2: RMB empty canvas → Add Comment.
- Path 3: Paste from clipboard.

New comment immediately enters inline rename mode.

### Comments — color sequence

Default colors cycle: Blue → Green → Yellow → Orange → Red → Purple → Cyan → Brown.

### Comments — interactions

- LMB click header: select + drag-comment.
- LMB click body: passes through to underlying entities (effectively empty canvas).
- LMB double-click header: inline rename.
- LMB drag handle: resize.
- RMB header: context menu.
- Hover header: grab cursor.

### Move with contents

- At drag start, snapshot nodes fully enclosed by comment.
- Those nodes pin to comment's relative position during drag.
- Nodes that "fall in" during drag do not join.
- Shift: move comment alone (no contents).
- Alt: move only contents (comment stays).
- Per-comment `MoveWithContents` toggle in Details panel.

### Comments — text

Single field. `\n` supported for multi-line. Header strip grows to fit.
**Do not implement separate body text.**

### Comments — z-ordering

Higher ZOrder draws later (on top). Bring to Front / Send to Back assign
extremes. Renormalize occasionally.

### Reroutes — model

Stored inside `Link.Waypoints` (list of Vector2 positions). Reroute is NOT
a separate graph entity at the data layer.

```csharp
public sealed record Link(LinkId Id, PinId FromPin, PinId ToPin,
                         IReadOnlyList<Vector2> Waypoints);
```

### Reroutes — visual

- Small filled circle (~12 px diameter), color = wire color.
- Slightly darker outline.
- Selection outline when selected.

### Reroutes — interactions

- LMB click: select.
- LMB drag: move; both wire segments follow.
- LMB double-click: remove reroute, wire segments merge.
- RMB: context menu (Delete, Cut/Copy/Duplicate).
- Alt+click: remove reroute.

### Reroutes — creation

- Path 1: Double-click on wire.
- Path 2: RMB wire → Insert Reroute Node Here.
- Path 3: Wire drag → drop on empty canvas → picker offers "+ Add Reroute"
  at top.

### Reroutes — typing

Implicitly typed by the wire. No type conversion. Color matches wire.

## 23. Bookmarks

### Data model

```csharp
public sealed record Bookmark(
    string BookmarkId,
    GraphId TargetGraph,
    string Label,
    Vector2 ViewportPan,
    float ViewportZoom,
    int SlotNumber,           // 1-9 for hotkey-bound; 0 for unbound
    DateTime CreatedAt);
```

Bookmarks live in **editor session state** (per-user). Not committed to
asset.

### Interactions

- **Ctrl+Shift+1..9:** set bookmark in that slot to current viewport.
  Prompt to confirm if slot occupied.
- **Ctrl+1..9:** jump to bookmark.
- **Cross-graph jump:** if bookmark is in different graph, opens that graph.
- Camera animates ~180ms.

### Scope

**Global per asset.** Cross-graph jumps are expected.

### Visual indicators

- Off-screen bookmark in current graph: edge marker arrow with tooltip.
- Maximum 9 edge markers (only slot 1-9; unbound bookmarks don't show).

### Persistence

Saved in editor session state. Orphaned bookmarks (target graph deleted)
silently removed on next asset open.

## 24. Hot-reload indicators

### External change event

```csharp
public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
    string? Reason);
```

### Editor response

1. **Selection preserved as much as possible.** Removed entities dropped from
   selection silently.
2. **Viewport unchanged.** No auto-jump.
3. **Affected nodes get badges**, fade over 2 seconds:
   - Added: green "+" badge.
   - Removed: red fade-out animation (~300ms).
   - Modified: yellow "Δ" badge.
   - Moved: animate to new position over 180ms; no badge.
4. **Toast notification:** "↻ Asset reloaded. 3 added, 1 removed, 2 modified".
   Auto-dismiss 5s.
5. **Details panel updates** if its target was modified.
6. **Undo stack invalidated** for commands touching removed entities. On
   Wholesale change, clear whole undo stack.

### Stale breakpoints

(For hosts providing `IDebugSession`.)
- Yellow filled circle (instead of red) on the node header.
- Tooltip: "Breakpoint stale. Click to rebind or remove."
- RMB options: Rebind, Remove, Show what changed.

### Conflict handling

When user has unsaved local changes AND external reload is triggered:
**block the reload.** Toast: "External changes detected. Save or discard
your changes to reload." User decides explicitly.

## 25. Debug visualization

When `IDebugSession.IsAttached`:

### Currently executing node

- Bright outline pulsing at ~2 Hz (sine alpha).
- Header glow overlay.
- If off-screen: edge marker arrow.

### Recently executed wires

- Brighter, slightly thicker.
- Animated dash flows along wire (~150 px/sec, triangles every 16 px).
- Fades over ~800 ms.
- Cap at 20 simultaneous animated wires.

### Breakpoint markers

- Red filled circle on node header, left side, 16×16 px.
- Yellow with ⚠ inside if stale.
- Hover: tooltip with hit count.

### Watch markers

- Eye icon next to watched pin.
- When paused, current value inline (truncated to 20 chars).

### Pause overlay

- Canvas tint desaturates by ~15%.
- Floating widget top-right: Resume / Step Over / Step Into / Step Out.

## 26. Command/indicator API

### `IEditorCommands`

```csharp
public interface IEditorCommands
{
    IReadOnlyList<EditorCommandDescriptor> All { get; }
    EditorCommandDescriptor? Get(string commandId);
    EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null);
    event Action<string>? AvailabilityChanged;
}
```

Command IDs the editor must publish: see kernel/CommandCatalog.cs.

The editor library publishes commands; the host's UI shell binds them to
buttons / menu items / hotkeys. The editor does NOT draw toolbars/menus
itself.

### `IEditorIndicators`

```csharp
public interface IEditorIndicators
{
    EditorStatusSnapshot Snapshot { get; }
    event Action? Changed;
    void Notify(EditorNotification notification);
}
```

Host's status bar reads `Snapshot` and renders however it wants.

### `EditorStatusSnapshot`

```csharp
public readonly record struct EditorStatusSnapshot(
    string? CurrentGraphName,
    int NodeCount,
    int SelectedNodeCount,
    int LinkCount,
    bool IsDirty,
    int ErrorCount,
    int WarningCount,
    float Zoom,
    Vector2 CanvasCursorPos,
    EditorMode Mode,
    string? CurrentTool);

public enum EditorMode { Editing, Compiling, Debugging, DebugPaused }
```

## 27. Performance budgets

| Phase | 500 nodes | 2000 nodes |
|---|---|---|
| Hit-testing | ≤ 0.1 ms | ≤ 0.2 ms |
| Spatial index update | ≤ 0.2 ms | ≤ 0.5 ms |
| Visible enumeration | ≤ 0.05 ms | ≤ 0.1 ms |
| Node rendering | ≤ 3 ms | ≤ 5 ms (low-zoom) |
| Wire rendering | ≤ 2 ms | ≤ 4 ms |
| ImGui submission | ≤ 1 ms | ≤ 2 ms |
| **Total canvas budget** | **≤ 6 ms** | **≤ 12 ms** |

Target: 60 FPS (16.6 ms total frame). ~4 ms reserved for host.

### Optimizations

- Virtualize: only render visible nodes (use spatial index).
- Low-zoom mode below 0.5×.
- Cache per-node measurements.
- Cache bezier sample points.
- Disable hover effects above 500 visible nodes.

## 28. Timing constants

(From `kernel/TimingConstants.cs` — authoritative)

| Event | Duration | Easing |
|---|---|---|
| Hover halo appear | 0 ms | — |
| Hover halo disappear | 80 ms | linear |
| Selection outline appear | 0 ms | — |
| Frame-to-X camera move | 180 ms | ease-out-cubic |
| Drag threshold | 4 px | — |
| Tooltip delay | 600 ms | — |
| Tooltip fade-in | 80 ms | linear |
| Wire connect snap | 120 ms | ease-out-cubic |
| Wire disconnect recoil | 120 ms | ease-out-cubic |
| Reroute insertion scale-in | 100 ms | ease-out-back |
| Node creation fade-in | 100 ms | ease-out-cubic |
| Node deletion fade-out | 80 ms | linear |
| Wire flow animation | 400 ms loop | linear |
| Executing pulse | 500 ms period | sine |
| Recently executed afterglow | 800 ms | ease-out |
| Pan inertia | 250 ms | ease-out-cubic |
| Popup open | 50 ms | ease-out |
| Popup close | 80 ms | linear |
| Context menu open | 50 ms | linear |
| Toast lifetime | 3000 ms | — |

## 29. Color conventions

### Pin/wire colors (default)

Defined in `kernel/DefaultTypeColors.cs`. Host may override via
`ITypeSystem.GetPinColor`.

| Type | Color |
|---|---|
| bool | red (#E74C3C) |
| byte / short / int / long | cyan (#5DADE2) |
| float / double | lime (#A6E22E) |
| string | magenta (#E84F8E) |
| Entity | blue (#3498DB) |
| Vector2/3/4 | yellow (#F1C40F) |
| Quaternion | yellow (#F1C40F) |
| Color | hot pink (#FF6B9D) |
| Guid | dark blue (#2C3E50) |
| struct (generic) | mid-blue (#5499C7) |
| Array<T> | inherits T's color |
| Map<K,V> | inherits V's color |
| Exec | white (#FFFFFF) |

### Category colors (node headers)

| Category | Color |
|---|---|
| Function | blue (#2E5C8A) |
| Event | red (#A93226) |
| Pure | green (#27AE60) |
| VariableGet | dark gray (#566573) |
| VariableSet | dark gray (#566573) |
| FlowControl | orange (#D35400) |
| Macro | purple (#8E44AD) |
| Custom | mid-gray (#7F8C8D) |

### Comment color palette (default)

8 colors cycled in creation order:
Blue (#4A90E2), Green (#7ED321), Yellow (#F8E71C), Orange (#F5A623),
Red (#D0021B), Purple (#9013FE), Cyan (#50E3C2), Brown (#8B572A).

### Editor accent / theme

Defaults from `kernel/DefaultTheme.cs`:
- Background: very dark gray (#1E1E1E)
- Grid minor: #2A2A2A
- Grid major: #3A3A3A
- Selection accent: yellow (#FFD700)
- Primary selection accent: brighter yellow + glow
- Error: red (#FF4444)
- Warning: yellow-orange (#FFAA00)
- Text default: light gray (#E0E0E0)
- Text muted: mid-gray (#808080)

## 30. Input abstraction

Editor uses `IInputSource` for canvas-level input. ImGui owns widget-level
input. When `ImGui.IsAnyItemActive()`, the canvas yields its frame.

```csharp
public interface IInputSource
{
    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    float WheelDelta { get; }

    bool IsMouseDown(MouseButton btn);
    bool IsMousePressed(MouseButton btn);
    bool IsMouseReleased(MouseButton btn);
    bool IsMouseDoubleClicked(MouseButton btn);

    bool IsKeyDown(EditorKey k);
    bool IsKeyPressed(EditorKey k, bool allowRepeat = false);
    bool IsKeyReleased(EditorKey k);

    KeyModifiers Modifiers { get; }
    ReadOnlySpan<char> TextThisFrame { get; }

    string? ReadClipboardText();
    void WriteClipboardText(string text);
}

public enum MouseButton { Left, Right, Middle, X1, X2 }

[Flags] public enum KeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Super = 8 }

public enum EditorKey
{
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Tab, Space, Enter, Escape, Backspace, Delete, Home, End, PageUp, PageDown,
    Left, Right, Up, Down, Insert, CapsLock,
    LeftBracket, RightBracket, Comma, Period, Slash, Minus, Equals, Apostrophe,
}
```

Two adapters ship:
- `RaylibInputSource` in `NodeEditor.Demo` (also used by real host).
- `FakeInputSource` in `NodeEditor.Core.Tests` (scriptable for tests).

---

End of spec brief. For deeper rationale see `../specs/` files:
- `A-canvas-interactions.md`
- `B-mini-editors.md`
- `C-picker.md`
- `D0-action-api.md`
- `D1-custom-events.md`
- `D2-functions.md`
- `D3-macros.md`
- `D4-find.md`
- `D6-my-blueprint-panel.md`
- `D7-details-panel.md`
- `D8-comments-reroutes.md`
- `D9-bookmarks.md`
- `D10-hot-reload.md`
