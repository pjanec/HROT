# Part C — The Generic Picker

## Why this is the densest part of the spec

The picker is one widget, opened in twelve+ different contexts. Get it right once, get it right everywhere. Get it wrong and the editor feels like a maze of inconsistent dropdowns.

Usage table:

| Trigger | Source key | Filter context | Selection |
|---|---|---|---|
| Tab on empty canvas | `nodes.all` | (none) | Single, places node |
| Drop wire on empty canvas | `nodes.by-pin` | Compatible-with-source-pin | Single, places + connects |
| RMB pin → Promote to Variable | (form, not picker) | — | — |
| Promote to Variable type | `types.all` | (none) | Single |
| Click inline asset-ref editor | `assets.by-type` | Type-compatible | Single |
| Click inline entity-ref editor | `entities.all` | Component constraints | Single |
| Click large-enum editor | `enum.values` | (none) | Single |
| Click `[Flags]` enum editor | `enum.values` | (none) | Multi-select |
| Click variable name | `variables.in-scope` | Type-compatible | Single |
| Function call node — pick function | `functions.all` | (none) | Single |
| Channel-action picker | `channels.actions` | Channel type | Single |
| ECS component picker | `components.all` | Tag/data constraints | Single |
| Event name picker | `events.engine` | Channel | Single |

12+ uses, same widget, different `IPickerSource<T>`.

## C.1 What the picker is

A floating, focused, search-driven selection UI that appears when the user needs to pick **one item** (or sometimes many) **from a long list** of typed things.

**Floating mini-window, not an ImGui popup.** Confirmed in design conversation. Popups have known issues with arrow-key navigation and persistent focus; a real window with its own focus model is necessary.

## C.2 Anatomy

```
┌──────────────────────────────────────────────────────────────────────┐
│ Add Node                                                          ✕  │
├──────────────────────────────────────────────────────────────────────┤
│ 🔍  [ vec3 mul                              ]   [ ☐ Context-aware ]  │
├──────────────────────────────────────────────────────────────────────┤
│  ★ Favorites                                                         │
│  ──────────────────────                                              │
│  ▸ Math/Vector                                                       │
│      Vector3 × Vector3                  ◯ Vec3  ◯ Vec3  →  ◯ Vec3   │
│    │ Multiply (Vector × Vector)         ◯ Vec3  ◯ Vec3  →  ◯ Vec3   │
│      Vector3 × Float                    ◯ Vec3  ◯ float →  ◯ Vec3   │
│  ▸ Math/Float                                                        │
│      Multiply                           ◯ float ◯ float →  ◯ float   │
│  ⌚ Recent                                                           │
│      Multiply (Vector × Vector)                                      │
├──────────────────────────────────────────────────────────────────────┤
│  Multiply (Vector × Vector)                                          │
│  ─────────────────────────                                           │
│  Multiplies two vectors component-wise.                              │
│                                                                       │
│  Pins:                                                                │
│    A : Vector3                                                        │
│    B : Vector3                                                        │
│    Result : Vector3                                                   │
│                                                                       │
│  Category: Math/Vector                                                │
│  Keywords: multiply, mul, vector, *                                  │
├──────────────────────────────────────────────────────────────────────┤
│  ↑↓ Navigate    ⏎ Select    Esc Cancel    Ctrl+Space Toggle Context  │
└──────────────────────────────────────────────────────────────────────┘
```

Width: ~520 px. Height: ~520 px (with preview pane) or ~400 px (compact mode). **Resizable**, persists size per source kind.

### Components

- **Title bar** — drags the window. ✕ closes. Title from `IPickerSource<T>.Title`.
- **Search input** — keyboard focus always lands here on open. Single-line text field. `🔍` icon is cosmetic.
- **Context toggle** — checkbox: `Context-aware` (default ON when wire-dragged, OFF for fresh Tab). Toggle with `Ctrl+Space`.
- **Result list** — scrollable, virtualized. Each row via source's `RenderItem`. Selected row has brighter background.
- **Section headers** — non-selectable rows: `★ Favorites`, `⌚ Recent`, category names. Collapsible.
- **Preview pane** — right side (or bottom in compact-tall layout). Shows extended info via source's `RenderPreview`. Updates as the user arrows through.
- **Footer with shortcuts** — fixed hint strip. Updates contextually (multi-select shows `Space Toggle`, etc.).

## C.3 Opening, closing, lifecycle

### Opening

```csharp
picker.Open<NodeCatalogEntry>(
    source: _nodeCatalogPickerSource,
    screenPos: cursorScreen,
    onPick: result => commandSink.Apply(new GraphCommand.AddNode(result.Kind, canvasPos)),
    onCancel: () => { /* nothing */ },
    initialQuery: ""
);
```

The picker:

1. Positions itself with top-left at `screenPos`, **clamped** to stay fully on-screen.
2. Takes keyboard focus on the search input.
3. Loads favorites and recents from persistent storage (keyed by source type).
4. Runs an initial empty-query fetch so the list is populated immediately.
5. If `initialQuery` provided, seeds the search field.

### Position clamping

```
desired = screenPos
size = (520, 520)
clamped.x = clamp(desired.x, 0, viewport.width - size.x)
clamped.y = clamp(desired.y, 0, viewport.height - size.y)
```

If the click was near the right edge, picker slides left. Near the bottom, slides up. Never partially off-screen.

### Closing

The picker closes when:

- User selects an item (Enter or click) → invokes `onPick` then closes.
- User presses Esc → invokes `onCancel` then closes.
- User clicks outside the picker window → same as Esc.
- Tab key when search input is unfocused → cycles focus *within* the picker. Doesn't close.

While the picker is open, **the canvas does not process input**. Canvas treats the picker as a focus-stealing overlay (just like a context menu). All key events route to the picker first.

### Modality

**Soft-modal:** floats above the editor, but the rest of the app keeps running (animations, debug-flow updates continue underneath). Only one picker open at a time. Opening a new one closes any existing.

## C.4 The search input — typing behavior

### Live filtering

Every keystroke triggers a refilter. No search button.

For expensive sources, there's a **debounce of ~120 ms** — wait for typing to pause before re-querying. The source declares this:

```csharp
public interface IPickerSource<TItem>
{
    QueryCost Cost { get; }   // Cheap (immediate) | Moderate (60ms debounce) | Heavy (120ms)
}
```

During debounce, show a subtle spinner in the search input's right edge.

### Matching algorithm

A proper fuzzy matcher. Implementation in `K04_fuzzy_matcher.md`. Algorithm tier in preference order:

1. **Exact match** of entire search string against display name (case-insensitive). Score ~10000.
2. **Display name prefix match.** Score ~5000 + (1000 - prefix-length-bonus).
3. **Word-start match.** Search `vm` matches `**V**ector **M**ultiply`. Score ~3000.
4. **CamelCase / underscore boundary match.** `MTV` matches `**M**ake**T**ransform**V**ector`. Score ~2500.
5. **Substring match in display name.** `ult` matches `M**ult**iply`. Score ~1500.
6. **Substring match in keywords or aliases.** Score ~1000.
7. **Fuzzy character-order match.** `vmu` matches `**V**ector **Mu**ltiply` (all chars appear in order). Score = remaining distance penalty.
8. **No match.** Excluded.

Within each tier:
- **Recently-used boost:** +500 score if used in the last 30 days; +1000 if within 24 hours.
- **Favorites boost:** +2000 score.
- **Shorter display names** win ties.

Matcher returns `(score, item, matchedCharPositions[])`. Matched positions drive character-by-character highlighting in the result rows (VSCode-style).

### Special prefixes (power-user)

| Prefix | Meaning |
|---|---|
| `>` | Search by category only (`> math` lists everything in Math/*) |
| `:` | Search by exact pin type (`:vector3` lists items with a Vector3 pin) |
| `#` | Search by tag/keyword only (skip display name) |
| `?` | Help mode — shows shortcuts and prefix reference |

Don't go overboard — too much DSL flavor confuses casual users. Implement only the four above; match Unreal where Unreal has them.

### Tab-completion

If the user types a partial word and there's a unique extension (e.g., `vec3` → only `Vector3` matches), pressing Tab in the search field auto-completes.

## C.5 Keyboard navigation — every key

| Key | Action |
|---|---|
| `↑` / `↓` | Move highlighted row up/down. Wraps at ends (configurable; off by default). |
| `Page Up` / `Page Down` | Move by visible-list-height. |
| `Home` / `End` | Jump to first / last result. |
| `Enter` | Select the highlighted row → invoke `onPick`. |
| `Esc` | Cancel → invoke `onCancel`. |
| `Tab` | If search has text and only one match: complete the search. Otherwise: cycle focus (search → toggles → search). |
| `Shift+Tab` | Reverse cycle. |
| `Ctrl+Space` | Toggle "Context-aware" filter. |
| `Ctrl+F` | Same as `Ctrl+Space`. |
| `Ctrl+↑` / `Ctrl+↓` | Move between section headers. |
| `Alt+↑` / `Alt+↓` | Move between matched items only (skip section headers). |
| `Space` | (Multi-select only) Toggle selection of highlighted row. |
| `Ctrl+Enter` | (Multi-select only) Accept all currently selected. |
| `F1` / `?` | Open help overlay showing all shortcuts. |
| `Ctrl+Backspace` | Delete word-back in search field. |
| `Ctrl+A` | Select all text in search field. |
| `Insert` | Pin/unpin highlighted item as favorite. |
| `Delete` | (For sources that allow it) remove from recent / favorites. |

### Mouse behaviors

| Action | Result |
|---|---|
| Click on a result row | Highlight it. Single-click does **not** confirm. |
| Double-click on a result row | Select (= Enter). |
| Click on a section header | Collapse/expand the section. |
| Click on the favorite star ☆/★ | Toggle favorite status. |
| Click outside picker | Same as Esc. |
| Mouse wheel over the list | Scroll. |
| Mouse wheel + Ctrl | Change preview pane visibility / size. |

**Why single-click doesn't confirm:** users often want to *see the preview* of an item without committing. Double-click for commit (or Enter on highlighted) is the universal convention.

## C.6 Result list rendering

### Layout per row

~28 px tall (compact) or ~40 px tall (with secondary info).

For a node-catalog entry:

```
[icon] Display Name Here              ◯Type→◯Type ⓘ
       Category / breadcrumb
```

- **Icon** (16×16): from `entry.IconKey`, resolved via `IIconProvider`. Falls back to default per-category glyph.
- **Display name**: matched-text highlighting renders here.
- **Pin preview**: tiny colored circles for first 1–2 inputs and 1 output (truncate if more).
- **ⓘ / ★**: hover icons for "info" (jump to preview pane) and "favorite" toggle.
- **Secondary line** (optional, expanded mode): category breadcrumb in dimmer text.

### Highlighting

- **Hover:** background tint, slightly brighter.
- **Keyboard-focused** (the navigated row): brighter accent + thin colored border on left edge. Visible even when mouse is elsewhere.
- **Both at once:** combines.
- **Matched characters:** accent color, slightly brighter/bolder.

### Section headers

Non-selectable rows. Distinct from items:

```
▾ Math / Float                          (12)    ← collapsed: ▸ Math/Float        (12)
```

- Triangle marker (▾/▸).
- Click to fold/unfold.
- Count in parentheses.
- When collapsed, **search overrides collapse state** — hidden items still appear in fuzzy results if they match. Otherwise users get confused when "multiply" returns no results despite typing it.

### Empty state

```
            ✦ No results for "wuznit"

            Try:
            • Check spelling
            • Toggle "Context-aware" off
            • Search by category: > math

            ─────────────────────────

            Recent picks (you might like):
            • Multiply
            • Make Vector3
```

Never leave the user looking at an empty screen.

### Section ordering

Result list groups items in this fixed order:

1. **★ Favorites** (if any apply to current query)
2. **⌚ Recent** (top 5 most-recently used matching items)
3. **Category sections** in natural hierarchy
4. **Deprecated / archived** (collapsed by default)
5. **Incompatible** (only when "Context-aware" is OFF and incompatible matches exist)

If the same item appears in Favorites AND a category, show in Favorites only.

### Virtualization

The result list virtualizes — only ~30 visible rows render each frame. Use ImGui's `ImGuiListClipper`. Critical for 5000-entry catalogs (ECS components in a large game).

## C.7 The preview pane

Right-side (or bottom in compact-tall) pane showing extended info about the keyboard-focused item.

### Content (driven by `IPickerSource<T>.RenderPreview`)

For a node catalog entry:

```
Multiply (Vector × Vector)
────────────────────────────
Multiplies two vectors component-wise.

Pins:
  A : Vector3              ← input
  B : Vector3              ← input
  Result : Vector3         ← output

Category: Math/Vector
Keywords: multiply, mul, vector, *
```

For an entity picker entry:

```
Player_01
─────────
Entity ID: #42, gen=3
Components: Transform, Health, Inventory, …

Position: (12.5, 0.0, -3.2)
Health: 87/100
─────────
Last modified: 2s ago
```

For an asset picker entry:

```
[thumbnail]   PrincessMaria.character
              ─────────────────────
              Character Asset
              Size: 4.2 MB
              Path: /Assets/Characters/Heroes/
              Tags: hero, melee, female

              Used by 12 entities.
```

The source has total freedom; it gets an `IPreviewRenderContext` with draw list access, layout helpers, and the picker's frame dimensions.

### When the preview pane is shown

- Always, unless the picker is in compact mode.
- Compact: when picker window width is below ~360 px, or when `source.PreferCompactLayout = true`.
- In compact mode: preview is on hover-over-row in a tooltip-style overlay, not a fixed pane.

### Preview updates

The preview updates on highlight change with a brief crossfade (~80 ms). Debounce by ~30 ms so rapid arrow-key scrolling doesn't thrash preview rendering.

### Lazy preview content

Expensive previews (3D thumbnails, registry lookups):

```csharp
public interface IPickerSource<TItem>
{
    bool IsPreviewExpensive(TItem item);
}
```

When expensive, preview renders a quick placeholder (`Loading…`) and actual content arrives over the next frame or two. Keeps arrow-key navigation snappy.

## C.8 Favorites and recents

### Favorites

- User-marked. Persisted globally across editor sessions, keyed by source-type + item-key.
- Toggle via the ☆/★ icon on a row, or `Insert` keyboard shortcut.
- Dedicated `★ Favorites` section at the top.
- Limit: 50 per source. After that, oldest gets removed when new one added (with toast warning).

### Recents

- Auto-tracked. Last N (default 20) selected items per source.
- Sort by recency (most recent first).
- `⌚ Recent` section at top (below Favorites).
- Limit: 20. Auto-evicts oldest beyond.
- Right-click section header → `Clear Recent`.

### Persistence format

```json
{
  "sources": {
    "NodeEditor.Picker.NodeCatalogPickerSource": {
      "favorites": ["NodeKind:Math.Multiply", "NodeKind:Make.Vector3"],
      "recents": [
        {"key": "NodeKind:Branch", "lastUsed": "2026-05-21T10:42:18Z", "count": 47}
      ]
    },
    "NodeEditor.Picker.AssetReferencePickerSource:CharacterAsset": {
      "favorites": [],
      "recents": []
    }
  }
}
```

Stored in editor session state (per-user, not per-asset). Key includes subtype discriminator (e.g., per asset type).

## C.9 Multi-select mode

For `[Flags]` enums and "pick multiple components" sources.

### Activation

```csharp
public interface IPickerSource<TItem>
{
    PickerSelectionMode SelectionMode { get; }
}

public enum PickerSelectionMode { Single, Multi, MultiOrdered }
```

`MultiOrdered` is for cases where selection order matters (rare; "pick functions in execution order").

### Visual differences from single-select

- Each row has a checkbox on the left: `[☐]` unselected, `[☑]` selected, `[—]` partial (rare).
- Footer shows: `Ctrl+Enter Accept Selected`. Enter alone doesn't accept — too easy to accidentally commit with only one selected.
- Selection count in header: `3 of 142 selected`.
- `Clear Selection` button next to the count.

### Keyboard interactions

- `Space`: toggle selection of the keyboard-focused row.
- `Ctrl+Enter`: accept all selected → `onPick` invoked with `IReadOnlyList<TItem>`.
- `Shift+↑` / `Shift+↓`: extend selection (toggle each row as you move).
- `Ctrl+A`: select all currently-matching rows.

### Bulk selection summary

When >1 selected, show a small bar above the list:

```
3 selected: Multiply, Add, Subtract                         [Clear]
```

## C.10 Layout variations

Source declares its preferred layout:

```csharp
public interface IPickerSource<TItem>
{
    PickerLayout PreferredLayout { get; }
}

public enum PickerLayout
{
    Standard,    // search top, list left, preview right
    Compact,     // search top, list fills (no preview)
    Wide,        // search top, list+preview side-by-side, full window width
    Grid,        // results in a grid (visual asset pickers)
    Tree,        // when items have hierarchy and that's primary navigation
}
```

### Standard (most common)

```
┌────────────────────────────────────────────┐
│ Title                                   ✕  │
│ 🔍 [search             ] [☐ Context]       │
├────────────────────┬───────────────────────┤
│ list               │ preview               │
│                    │                       │
├────────────────────┴───────────────────────┤
│ ↑↓ ⏎ Esc                                   │
└────────────────────────────────────────────┘
~520 × 520 px
```

### Compact

```
┌────────────────────────────┐
│ Pick Color              ✕  │
│ 🔍 [search       ]         │
├────────────────────────────┤
│ list (no preview)          │
└────────────────────────────┘
~320 × 360 px
```

### Wide (asset browsers with thumbnails)

```
┌─────────────────────────────────────────────────────────────┐
│ Pick Asset                                               ✕  │
│ 🔍 [search]  [▾ Type filter]  [☐ Show thumbnails]           │
├──────────────────┬──────────────────────────────────────────┤
│ list (thumbnails)│ preview (large)                          │
└──────────────────┴──────────────────────────────────────────┘
~800 × 600 px
```

### Grid (visual asset pickers)

```
┌──────────────────────────────────────────────────────────────┐
│ Pick Character                                            ✕  │
│ 🔍 [search]                                                  │
├──────────────────────────────────────────────────────────────┤
│ [🟦] [🟪] [🟥] [🟩] [🟧] [🟨]                                  │
│  Knight   Mage    Rogue    ...                               │
│ [🟦] [🟪] [🟥] [🟩] [🟧] [🟨]                                  │
└──────────────────────────────────────────────────────────────┘
```

Tiles are thumbnails with labels. Same keyboard nav (arrows in 2D grid).

### Tree (hierarchical sources)

```
┌────────────────────────────────────────────┐
│ Pick Component Type                     ✕  │
│ 🔍 [search]                                │
├────────────────────────────────────────────┤
│ ▾ Fdp.Core.Components                      │
│   ▾ Spatial                                │
│       Transform                            │
│       BoundingBox                          │
│   ▾ Combat                                 │
│       Health                               │
│       WeaponChannel                        │
│ ▸ Game.Components                          │
│ ▸ Third-Party                              │
└────────────────────────────────────────────┘
```

Arrow keys expand/collapse (←/→) and navigate (↑/↓). Search auto-expands matching branches.

## C.11 Special-case variants

### Promote-to-Variable picker (form, not a picker)

Two-step inline form using picker window chrome:

```
┌────────────────────────────────────────────┐
│ Promote to Variable                     ✕  │
├────────────────────────────────────────────┤
│ Pin Type: Vector3                          │
│                                            │
│ Variable Name:                             │
│ [ TargetPosition                        ]  │
│                                            │
│ Variable Scope:                            │
│ ◉ Member (asset-level)                     │
│ ○ Local (graph-level)                      │
│                                            │
│ Variable Category:                         │
│ [ Combat ▾ ]                               │
│                                            │
│ ☐ Expose on Spawn                          │
│ ☐ Editable                                 │
├────────────────────────────────────────────┤
│         [ Cancel ]    [ Create & Wire ]    │
└────────────────────────────────────────────┘
```

Not strictly a picker — it's a small form. But reuses picker window chrome, focus model, Esc-cancel. Same `PickerWindow` chrome, varied body.

Name field validation: live red border + inline error on conflict.

### Channel-action picker (with rich preview)

```
┌────────────────────────────────────────────┐
│ Pick Locomotion Action                  ✕  │
│ 🔍 [search]                                │
├────────────────────┬───────────────────────┤
│ ⌚ Recent           │ MoveTo                │
│   MoveTo           │ ─────                 │
│ ──────             │ Move to a target      │
│ Actions:           │ world position.       │
│   Idle             │                       │
│   MoveTo           │ Parameters:           │
│   MoveToEntity     │   TargetPosition: Vec3│
│   Halt             │   Speed: float        │
│   Patrol           │   Tolerance: float    │
└────────────────────┴───────────────────────┘
```

Preview shows the action's parameter schema — user knows what pins will appear after picking.

### Type picker with container recursion

For "promote to variable → choose type" or wildcard freezing:

```
┌────────────────────────────────────────────┐
│ Pick Type                               ✕  │
│ 🔍 [vec3                ]                  │
├────────────────────┬───────────────────────┤
│ Primitives:        │ Vector3               │
│   bool             │ ─────                 │
│   int              │ 3-component float     │
│   float            │ vector. 12 bytes.     │
│   string           │ Pin color: ●          │
│ Math:              │ Default editor:       │
│   Vector2          │   X: 0  Y: 0  Z: 0    │
│   Vector3          │                       │
│   Vector4          │                       │
│   Quaternion       │                       │
│ Containers:        │                       │
│   Array<…>         │                       │
│   Map<…, …>        │                       │
└────────────────────┴───────────────────────┘
```

Container types (`Array<…>`, `Map<…, …>`) when selected **push a nested view in-place** (not opens a new picker). Recursive composition. Esc backs out one level.

Confirmed in design conversation: push-in-place is the model. The picker tracks a navigation stack of "levels"; Esc pops one level; cancelling on the outermost level closes the picker.

## C.12 Picker registry

```csharp
public interface IPickerRegistry
{
    void Register<TItem>(string sourceKey, IPickerSource<TItem> source);
    IPickerSource<TItem>? Get<TItem>(string sourceKey);

    // Open by name — the editor uses this for built-in actions
    void Open(
        string sourceKey,
        Vector2 screenPos,
        Action<object> onPick,
        Action? onCancel = null,
        IReadOnlyDictionary<string, object?>? context = null);
}
```

Sources registered by string key, not type alone — same `TItem` may have multiple meaningfully-different sources.

### Built-in source keys

| Key | Source | Notes |
|---|---|---|
| `nodes.all` | All node kinds | Fallback when no context |
| `nodes.by-pin` | Context-filtered nodes | Used on wire-drag-release |
| `nodes.by-target` | Filtered by clicked target | For "add node calling this object" |
| `types.all` | All types in `ITypeSystem` | For type pickers |
| `variables.all` | All variables in current asset | Member + local |
| `variables.in-scope` | Visible from current graph | Per-graph |
| `functions.all` | All callable functions | For function-call nodes |
| `assets.by-type` | Per asset-type | Host registers one per type |
| `entities.all` | All entities in world | Debug-attached only |
| `components.all` | All component types | For ECS-aware nodes |
| `events.engine` | Engine event catalog | For event-listener nodes |
| `channels.actions` | Per channel type | Host registers one per channel |
| `enum.values` | Per enum type | Host registers per `[Flags]`/large enum |
| `colors.named` | Named color presets | Default ships with editor |

Host registers sources at startup. Picker UI doesn't change based on source — only the data does.

## C.13 Async loading

For slow-source-backed pickers (filesystem scan, network call, ECS world query):

```csharp
public interface IPickerSource<TItem>
{
    IReadOnlyList<TItem> Query(string text, IReadOnlyDictionary<string, object?>? context);

    Task<IReadOnlyList<TItem>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct);

    bool IsAsync { get; }
}
```

When `IsAsync` is true:
- Picker shows spinner in search field while query is in-flight.
- Results list shows previous-query results, slightly dimmed, until new ones arrive.
- A new query cancels the previous (via `CancellationToken`).

Typical case (in-memory catalogs): `IsAsync = false`, sync path used.

## C.14 Drag-and-drop

### Drag IN

User drags an asset from external asset browser onto a picker. If source accepts (via `source.CanAcceptDrop(payload)`), the dropped item is selected immediately. Closes picker.

### Drag OUT

User drags a row from the picker onto the canvas. Closes picker, places node at drop position (for node-catalog sources). Alternative to Enter — some users prefer drag-onto-canvas.

For inline-pin-editor pickers, drag-out doesn't make sense. Source declares:

```csharp
public interface IPickerSource<TItem>
{
    bool AllowsDragOut { get; }
    bool AllowsDragIn { get; }
}
```

Confirmed in design: include drag-out for the node-catalog picker. Cost ~30 LOC.

## C.15 New-item-name input

The picker supports creating new items via search text when no match exists. Pattern:

- Source declares `AllowsNewItemCreation = true`.
- When the user types a query that has no matches, the bottom of the result list shows:
  ```
  ✦ Create new: 'YourText'
  ```
- Selecting this row invokes `onPick` with a special `NewItem(text)` payload that the source's `onPick` handler creates.

Used for variable name creation, custom event creation, category creation. Confirmed in design as Unreal-style behavior.

## C.16 Sticky vs auto-jump highlight on query change

Confirmed: **auto-jump.** When the user types a search query, the highlighted row resets to the first result. This matches Unreal and most editors.

## C.17 "Context-aware" default state

Confirmed:
- When opened via wire-drag onto empty canvas: default ON (only compatible nodes).
- When opened via Tab from empty canvas (no context): default OFF (all nodes).
- Toggleable any time with `Ctrl+Space`.

## C.18 Error and edge-case behavior

| Situation | Picker response |
|---|---|
| Source's `Query` throws | Catch, show error row: `⚠ Query failed: {message}`. Log. Don't crash. |
| Source returns 10,000+ items | Virtualization handles it. Add footer: `showing top 1000 of 10,000 results`. |
| User types non-text key (e.g., Ctrl+F1) | Pass to keyboard nav; don't insert into search field. |
| Source unavailable on Open | Single-row error: `Picker source '{key}' is not registered`. |
| Item's `RenderItem` throws | Catch per-item; show placeholder `⚠ {item-key}`. Other rows still render. |
| Item's `RenderPreview` throws | Catch; preview pane shows `⚠ Preview unavailable`. |
| Window resized below minimum | Clamp at minimum (~280 × 240). Switch to compact layout. |
| Mouse off-screen during drag | No-op — picker is a window; drag continues. |
| Editor closes picker programmatically | `onCancel` invoked. |
| Picker open when graph tab is switched | Picker stays open but context is stale. Source detects via context comparison and self-closes if invalidated. |

## C.19 Performance budget

| Phase | Budget |
|---|---|
| Open / close animation | <50 ms |
| Per-frame render (full picker) | <2 ms |
| Query for in-memory source (1000 items) | <5 ms |
| Async query dispatch | <0.5 ms |
| Per-keystroke refilter | <10 ms (debounce kicks in if heavier) |
| Initial item-list render (virtualized) | <1 ms |
| Highlight character recomputation | <0.5 ms |

Key optimizations:
- Cache match-scored, sorted result list between frames if neither query nor source changed.
- Cache `GetSearchableText` results per item per session.
- Virtualize the list (ImGui's `ImGuiListClipper`).
- Lazy-render preview for expensive items.

## C.20 Accessibility touches

- Every keyboard shortcut in the footer is in the `?` help overlay.
- High-contrast mode: editor honors color-blind palette via `ITypeSystem.GetPinColor` returning shifted hues.
- Right-click on result row → small menu: `Pin as Favorite`, `Copy Name`, `Show in Documentation`, `Show in Source Tree`. Sources may add more.
- Keyboard hint footer fades to ~50% opacity after the user has used the picker 10+ times (count is persisted).
- Hover tooltip on "Context-aware": `Filtered by the pin you dragged from. Click to see all options.`
