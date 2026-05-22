# T-14 — UI: Generic Picker Window

## Goal
A **single** floating window component reused for 12+ different picking contexts (node-search, variable, type, asset, entity, enum value, channel action, etc.). Driven entirely by a data-shaped request submitted by the caller.

## Project
`NodeEditor.UI`

## References

**Specs (full read required):**
- `../specs/C-picker.md` — complete normative behavior, 5 layouts, 12 contexts
- `../instructions/01-spec-brief-part2.md` §16 (search popup), §17 (picker invocations across the editor)

**Kernel:**
- `../kernel/03-search-spatial-constants.md` — `FuzzyMatcher` (search ranking)
- `../kernel/01-interfaces.md` — `IPickerRegistry`

## Concept

The host issues a `PickerRequest` to `IPickerRegistry`. The editor opens a single window and renders one of five layouts:

| Layout | Use cases |
|---|---|
| **Standard** | Variable picker, type picker, function picker (single column list with description side panel) |
| **Compact** | Enum value picker, channel action picker (single column, no side panel) |
| **Wide** | Node-search picker on dropped-wire (two columns: hierarchical category tree + list) |
| **Grid** | Asset picker with thumbnails |
| **Tree** | Type-system browser (recursive expand/collapse) |

Each picker shares: a search box at the top using `FuzzyMatcher`, a "Favorites" filter toggle, a "Recent" section, multi-select capability gated by request, OK/Cancel buttons, ESC to close, Enter to confirm.

## Deliverables

```
src/NodeEditor.UI/
    Picker/
        PickerRegistry.cs        // IPickerRegistry default impl
        PickerWindow.cs          // The single popup window
        PickerRequest.cs         // Input contract from caller
        PickerResult.cs          // Output payload to caller's continuation
        PickerEntry.cs           // One row in the list
        Layouts/
            StandardLayout.cs
            CompactLayout.cs
            WideLayout.cs
            GridLayout.cs
            TreeLayout.cs
        PickerState.cs           // Internal: search text, selection, scroll, etc.
        FavoritesStore.cs        // Per-key favorites persisted via host preferences
        RecentStore.cs           // Per-key recent items, capped to 16 most recent
```

## Public types

```csharp
namespace NodeEditor.UI.Picker;

/// <summary>
/// Layout choice for a picker invocation. Determines panel structure and per-item rendering.
/// </summary>
public enum PickerLayout { Standard, Compact, Wide, Grid, Tree }

/// <summary>
/// Caller-supplied input to open a picker. Fully data-driven — the picker doesn't know
/// what these items mean, just renders them and emits the choice.
/// </summary>
public sealed class PickerRequest
{
    /// <summary>Stable key identifying this picker context (used for favorites + recent persistence).</summary>
    public required string ContextKey { get; init; }

    /// <summary>Window title shown to the user.</summary>
    public required string Title { get; init; }

    /// <summary>Layout to use.</summary>
    public PickerLayout Layout { get; init; } = PickerLayout.Standard;

    /// <summary>Source of items. May be lazy (only walked as the user scrolls).</summary>
    public required Func<IEnumerable<PickerEntry>> ItemsProvider { get; init; }

    /// <summary>Allow selecting multiple entries (Ctrl/Shift+click).</summary>
    public bool AllowMultiSelect { get; init; }

    /// <summary>Initial search text (e.g. pre-filled with the type filter of a dropped wire).</summary>
    public string InitialQuery { get; init; } = "";

    /// <summary>If non-null, the screen position the window should anchor to (e.g. mouse position).</summary>
    public Vector2? AnchorScreen { get; init; }

    /// <summary>Optional category tree for Wide layout. Null for other layouts.</summary>
    public CategoryNode? CategoryRoot { get; init; }
}

/// <summary>One row/cell in a picker. Generic enough to support text + icon + sublabel + thumbnail.</summary>
public sealed record PickerEntry(
    string Id,                // stable identity for favorites/recent
    string Name,              // primary label, used for search
    string? Description,      // long description shown in detail pane
    string? Category,         // categorization path "A/B/C" used for Wide/Tree layouts
    IReadOnlyList<string>? Keywords,  // additional search terms
    IntPtr? IconTextureId,    // for Grid layout thumbnails (or icons inline)
    object? Tag                // opaque caller payload returned via PickerResult
);

/// <summary>Category tree node used by Wide layout (sidebar tree).</summary>
public sealed record CategoryNode(string Name, IReadOnlyList<CategoryNode> Children);

/// <summary>What the user picked. Empty list = cancelled.</summary>
public sealed record PickerResult(IReadOnlyList<PickerEntry> Selection)
{
    public bool Cancelled => Selection.Count == 0;
    public PickerEntry? First => Selection.Count > 0 ? Selection[0] : null;
}
```

## Behavior contract

- **Search box** is focused on open.
- Search ranking uses `FuzzyMatcher.Rank(query, name, keywords)`; results are sorted by score desc, then name asc.
- A score of zero hides the entry.
- **Recent + Favorites pinning**: regardless of search query, favorites and recent entries always appear at top with subdued sections labels "★ Favorites" and "↻ Recent". When the query is empty, these are the *only* sections shown initially.
- **Favorites**: starred via right-click → "Favorite". Persisted via host preferences (`IEditorHostServices.Preferences.Get/Set` if available; otherwise via `FavoritesStore` fallback to an in-memory dict).
- **Recent**: top-16 most recently picked entries; updated on confirm.
- Multi-select: Ctrl+click toggles, Shift+click range-selects (only meaningful within Standard/Compact layouts). Enter confirms current selection. Disabled if `AllowMultiSelect=false`.
- Hotkeys:
    - Arrow keys / PgUp / PgDn / Home / End — navigate.
    - Enter — confirm.
    - ESC — cancel (returns empty selection).
    - Right-click on an entry — context menu with "Favorite/Unfavorite", "Copy ID", "Show in Type Browser" (last is conditional, layout-dependent).
- Window dismiss conditions: ESC, click outside window, focus another window, Enter confirm.

## Registry interface

The interface lives in `kernel/01-interfaces.md`. Default implementation here:

```csharp
namespace NodeEditor.UI.Picker;

public sealed class PickerRegistry : IPickerRegistry
{
    private readonly PickerWindow _window = new();

    /// <summary>
    /// Open the picker for a given request and call <paramref name="onChosen"/> when the user
    /// confirms or cancels. Multiple sequential calls cancel previous open pickers.
    /// </summary>
    public void OpenPicker(PickerRequest request, Action<PickerResult> onChosen)
    {
        _window.Open(request, onChosen);
    }

    /// <summary>Per-frame draw call from the host. Renders the active picker if any.</summary>
    public void DrawFrame()
    {
        _window.DrawFrame();
    }
}
```

The interface signature in `kernel/01-interfaces.md` is the source of truth — adapt if any signatures differ.

## Window structure (orientation, not strict layout)

```
┌─────────────────────────────────────────────┐
│ [search box] [favorites toggle]             │
├──────────────┬──────────────────────────────┤
│ Categories   │ Items (filtered + ranked)    │
│ (Wide only)  │                              │
│              │                              │
├──────────────┴──────────────────────────────┤
│ Detail pane                                 │
│ (Standard only — name, description, etc.)   │
├─────────────────────────────────────────────┤
│ [OK]  [Cancel]                              │
└─────────────────────────────────────────────┘
```

Default sizing: 720x520. Position: anchored at `AnchorScreen` if provided, otherwise centered in main viewport. Clamped to stay on-screen.

## Layouts — render hints

**Standard**: vertical list left half, detail pane right half. Detail shows: bold name, italic category breadcrumb, description text, tag/keyword chips.

**Compact**: vertical list only, fills full window. No detail pane. Window default size shrunk to 360x420.

**Wide**: category tree (left, 240px), list (right, fills). Description shown inline as 2nd line under each list entry, dim color. Use this for "drop-wire opens picker" — fully populated catalog.

**Grid**: 4 columns × N rows of thumbnail tiles, each 128x144 px. Thumbnail on top, name below. Hovered tile gets highlight. Detail pane shown at bottom (full width, 80px tall).

**Tree**: indented expandable list. Tree uses `Category` strings split on `/` to build hierarchy if `CategoryRoot` is null; otherwise uses the explicit tree.

## Performance

- The provider is called lazily; pre-cache its output on first call into a `PickerEntry[]` field.
- Re-rank only when the search query or favorites/recent state changes (track a `_lastQuery` field).
- For >2000 entries, use ImGui's clipper (`ImGuiListClipper`).

## Acceptance

- Compiles.
- The demo app (T-21) has six scenarios that exercise:
    1. Standard layout — variable picker.
    2. Compact layout — enum value picker.
    3. Wide layout — node-search on dropped wire (uses `INodeCatalog`).
    4. Grid layout — asset picker (with placeholder gradient textures since the demo has no asset store).
    5. Tree layout — type browser.
    6. Multi-select — multi-pick "add input pins" scenario.
- Favorites and recent persistence works within a session (across sessions if Preferences is wired by the host).

## Estimated Size
~600 LOC.

## Status
Pending.
