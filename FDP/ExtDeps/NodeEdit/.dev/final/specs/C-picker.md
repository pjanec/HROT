# C — The Generic Picker

The picker is one widget, opened in 12+ different contexts. It MUST be
implemented once and parameterized; do not write separate popups per use
case.

## C.1 What is a picker?

A floating, focused, search-driven selection UI that appears when the user
needs to pick one (or sometimes many) typed items from a long list.

Use cases (all use the same picker):

| Trigger | Source | Selection |
|---|---|---|
| Tab on empty canvas | All node kinds | Single (places node) |
| Drop wire on empty canvas | Compatible nodes | Single (places + connects) |
| RMB pin → Promote to Variable | (form, not picker) | — |
| Promote to Variable type selection | All types | Single |
| Click inline asset-ref editor | Type-filtered assets | Single |
| Click inline entity-ref editor | Entities + constraints | Single |
| Click large-enum editor | Enum values | Single |
| Click [Flags] enum editor | Enum bit values | Multi-select |
| Click variable name | Variables in scope | Single |
| Function call node — pick function | Functions | Single |
| Channel-action picker | Channel action enum | Single |
| ECS component picker | All components | Single |
| Event name picker | Engine event catalog | Single |

## C.2 Window properties

- **Floating window**, NOT an ImGui popup.
- `ImGui.Begin(name, ref open, NoCollapse | NoSavedSettings)`.
- Title bar with × button (drag to move).
- Borders visible.
- **Always-on-top while open.**
- Default size 520 × 520 px. Resizable. Position persisted per source key.
- **Position clamping** at open: ensure fully on-screen.

## C.3 Layout

### Standard
```
┌────────────────────────────────────────────────────┐
│ Title                                          ✕   │
├────────────────────────────────────────────────────┤
│ 🔍 [ search                ]  [☐ Context-aware]   │
├────────────────────────────────┬───────────────────┤
│ list (virtualized, ~60%)        │ preview (~40%)   │
│ ★ Favorites                    │                   │
│ ⌚ Recent                      │                   │
│ ▾ Category 1                   │                   │
│     • Item                     │                   │
│ ▸ Category 2 (collapsed)       │                   │
├────────────────────────────────┴───────────────────┤
│ ↑↓ Navigate  ⏎ Select  Esc Cancel  Ctrl+Space Ctx │
└────────────────────────────────────────────────────┘
```

### Compact
```
┌────────────────────────────┐
│ Title                  ✕  │
│ 🔍 [search]                │
├────────────────────────────┤
│ list (no preview)          │
└────────────────────────────┘
~320 × 360
```

### Wide
```
~800 × 600 with larger preview pane
```

### Grid
```
┌──────────────────────────────────────────────────────────────┐
│ Title                                                     ✕  │
│ 🔍 [search]                                                  │
├──────────────────────────────────────────────────────────────┤
│ [🟦] [🟪] [🟥] [🟩] [🟧] [🟨]                                  │
│  Knight   Mage    Rogue    ...                               │
│ [🟦] [🟪] [🟥] [🟩] [🟧] [🟨]                                  │
└──────────────────────────────────────────────────────────────┘
```

Arrow keys move tile-to-tile in 2D grid.

### Tree
Hierarchical primary axis. Arrow keys expand/collapse (←/→) and navigate (↑/↓).

## C.4 Source interface

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
    bool AllowArbitraryTextInput { get; }   // for "create new" flows

    IReadOnlyList<TItem> Query(string text, IReadOnlyDictionary<string, object?>? context);

    Task<IReadOnlyList<TItem>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct);

    void RenderItem(TItem item, bool selected, bool keyboardFocused,
                    IPickerRenderContext ctx);
    void RenderPreview(TItem item, IPickerRenderContext ctx);
    bool IsPreviewExpensive(TItem item);

    string GetSearchableText(TItem item);
    string GetItemKey(TItem item);
    bool CanAcceptDrop(object payload);
}

public enum PickerLayout { Standard, Compact, Wide, Grid, Tree }
public enum PickerSelectionMode { Single, Multi, MultiOrdered }
public enum QueryCost { Cheap, Moderate, Heavy }    // controls debounce
```

## C.5 Registry

```csharp
public interface IPickerRegistry
{
    void Register<TItem>(string sourceKey, IPickerSource<TItem> source);
    IPickerSource<TItem>? Get<TItem>(string sourceKey);

    void Open(
        string sourceKey,
        Vector2 screenPos,
        Action<object> onPick,
        Action? onCancel = null,
        IReadOnlyDictionary<string, object?>? context = null);
}
```

### Built-in source keys

| Key | Purpose |
|---|---|
| `nodes.all` | Fallback when no context |
| `nodes.by-pin` | Context-filtered (used by wire drop) |
| `nodes.by-target` | Context-filtered for "calls on this target" |
| `types.all` | Type picker |
| `variables.all` | All variables in current asset |
| `variables.in-scope` | Variables visible from current graph |
| `functions.all` | All callable functions |
| `assets.by-type` | Per asset-type (host registers one each) |
| `entities.all` | All entities in world (debug only) |
| `components.all` | All component types |
| `events.engine` | Engine event catalog |
| `channels.actions` | Per channel type |
| `enum.values` | Per enum type |
| `colors.named` | Default ships with editor |

## C.6 Search behavior

### Live filtering

Every keystroke triggers refilter. No "Search" button.

### Debounce

By `QueryCost`:
- Cheap: 0 ms (immediate).
- Moderate: 60 ms.
- Heavy: 120 ms.

During debounce window, show subtle spinner in search input right edge.

### Async sources

`IsAsync = true`:
- Previous results dimmed during in-flight query.
- New query cancels previous via `CancellationToken`.

### Fuzzy matching

Tiered ranking (see `kernel/FuzzyMatcher.cs` for canonical impl):

1. Exact display name match → 10000.
2. Prefix match → 5000 + length bonus.
3. Word-start match (e.g., "vm" → "**V**ector **M**ultiply") → 3000.
4. CamelCase boundary match → 2500.
5. Substring in display name → 1500.
6. Substring in keywords/aliases → 1000.
7. Fuzzy char-order match → 500 + distance penalty.
8. No match → excluded.

Boosts:
- Recently used (last 24h): +1000.
- Recently used (last 30d): +500.
- Favorite: +2000.

Tiebreak: shorter names win.

Match positions returned for character highlighting in the row.

### Special prefixes

| Prefix | Meaning |
|---|---|
| `> category` | Search by category only |
| `: type` | Search by exact pin type |
| `# keyword` | Search keywords only (skip display name) |
| `?` | Help mode (shortcut + prefix reference) |

### Tab-completion

Single match by prefix → Tab autocompletes search text.

## C.7 Keyboard nav

(Full table in brief §16. Key points:)

- ↑/↓: navigate items, wrap optional.
- PgUp/PgDn: page navigation.
- Home/End: first/last.
- Enter: select highlighted.
- Esc: cancel.
- Tab: autocomplete or cycle focus (search → toggles → search).
- Ctrl+Space / Ctrl+F: toggle Context-aware.
- Ctrl+↑/↓: jump between section headers.
- Alt+↑/↓: jump skipping section headers.
- Space (multi-select): toggle highlighted.
- Ctrl+Enter (multi-select): accept.
- Shift+↑/↓ (multi-select): extend selection.
- Ctrl+A (multi-select): select all matching.
- Insert: pin/unpin highlighted as favorite.
- Delete: remove from recent / favorites (for sources that allow).
- F1 / ?: help overlay.

## C.8 Result list

### Row layout (Standard)

```
[icon] Display Name (highlighted matches)    ◯Type→◯Type ⓘ
        Category / breadcrumb (smaller, dimmer)
```

Components:
- 16×16 icon from source `RenderItem` (or category default).
- Display name with matched-character highlighting.
- Pin preview (small colored circles for first 1-2 inputs + 1 output).
- Optional `ⓘ` / `★` hover icons.

### Row states

- Hover: bg tint.
- Keyboard-focused (navigated): brighter accent + left border.
- Both: combines.
- Matched chars: brighter/bolder in display name.

### Section headers

Non-selectable rows. Collapsible.
```
▾ Math / Float                  (12)
```

When collapsed, hidden items still appear in fuzzy results that match
(search overrides collapse state).

### Empty state

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

### Section ordering

1. ★ Favorites (if any match)
2. ⌚ Recent (top 5 most recently used)
3. Category sections
4. Deprecated / archived (collapsed by default)
5. Incompatible (only when Context-aware OFF)

### Virtualization

Use `ImGuiListClipper`. Only ~30 visible rows rendered per frame.

## C.9 Preview pane

Right side (Standard / Wide) or bottom (Compact-tall) or hidden (Compact).

Driven by `IPickerSource<T>.RenderPreview`. Updates on highlight change
with ~80 ms crossfade. Debounce 30 ms during arrow-key bursts.

### Lazy preview

Sources expose `IsPreviewExpensive(item) → bool`. Expensive previews show
"Loading…" placeholder for first frame, then real content.

## C.10 Favorites and recents

### Favorites
- Marked via ☆/★ icon on row, or Insert key.
- Persisted globally per source-key.
- Limit 50 per source.

### Recents
- Auto-tracked. Last 20 selections per source.
- Sort by recency desc.
- Cleared via right-click section header → "Clear Recent".

### Persistence format

```json
{
  "sources": {
    "nodes.all": {
      "favorites": ["NodeKind:Branch", ...],
      "recents": [
        {"key": "...", "lastUsed": "2026-05-21T10:42:18Z", "count": 47}
      ]
    }
  }
}
```

Stored in editor session state.

## C.11 Multi-select mode

When `SelectionMode != Single`:

- Each row has checkbox `[☐]` / `[☑]` / `[—]` (partial).
- Header shows "**N** of M selected" + Clear button.
- Footer shifts hints (Space toggle, Ctrl+Enter accept).
- Selection summary above list:
  ```
  3 selected: Multiply, Add, Subtract                [Clear]
  ```

## C.12 Drag in/out

### Drag in

If source's `CanAcceptDrop(payload)` returns true, on drop the item is
selected and picker closes.

### Drag out

Drag row out of picker. Ghost row floats with cursor. On release outside
picker, closes; `onPick` called with the dragged item.

## C.13 Special variants

### Promote-to-Variable form

NOT a picker; a small modal form using the picker chrome.

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

Name validation: red border + inline "name taken" hint when invalid.

### Type picker

Nested. Selecting a container type (Array, Map) opens a nested picker for
the element type. Esc backs out one level.

### "Create new" flow

Source declares `AllowArbitraryTextInput = true`. If user's search text
doesn't match any existing item, show a placeholder row at top:

```
✦ Create new: "MyNewVariable"
```

Selecting it returns the typed text as the "new item key" to the caller.

## C.14 Error handling

| Situation | Behavior |
|---|---|
| Source `Query` throws | Show error row "⚠ Query failed: {msg}"; log; keep picker open. |
| Item `RenderItem` throws | Catch per-item; show placeholder "⚠ {key}". |
| Preview throws | "⚠ Preview unavailable". |
| Window resized below minimum | Clamp at 280 × 240; switch to compact. |
| Source unavailable on Open | Single error row "Picker source 'foo' is not registered". |
| Picker open when graph tab switches | Picker stays open but context may go stale; source self-closes if invalidated. |

## C.15 Performance budget

| Phase | Budget |
|---|---|
| Open / close animation | <50 ms |
| Per-frame render | <2 ms |
| Query (1000 items in-memory) | <5 ms |
| Async dispatch | <0.5 ms |
| Per-keystroke refilter | <10 ms |
| Initial list render (virtualized) | <1 ms |
| Highlight recomputation | <0.5 ms |

Optimizations:
- Cache match-scored result list between frames if query+source unchanged.
- Cache `GetSearchableText` per item per session.
- Virtualize via `ImGuiListClipper`.
- Lazy preview rendering.

## C.16 Test scenarios for the demo

Demo app implements 12 scenarios, each using a different source:

1. Add Node (Tab on canvas) — Standard, single.
2. Add Node from wire drag — Standard, single, filtered.
3. Pick Variable — Standard, single, with type icons.
4. Pick Type — Standard, single, nested.
5. Pick Color — Compact, single, named colors.
6. Pick Flags Enum — Compact, multi-select.
7. Pick Asset (thumbnails) — Grid, single.
8. Pick Component Type — Tree, single.
9. Pick Channel Action (preview) — Standard with rich preview.
10. Promote to Variable form — special variant.
11. Type picker → Container → Element type — nested recursion.
12. Heavy async picker — synthetic 5000 entries, 200 ms simulated latency.
