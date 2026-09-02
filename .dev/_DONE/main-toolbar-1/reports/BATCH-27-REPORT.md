# BATCH-27-REPORT: NodeEdit TreeLayout parity — type icons + match-highlight + folder icons + scroll

**Date:** 2026-06-12
**Status:** ✅ Complete — all tasks implemented, all tests green, demo builds clean

---

## Summary

Closed the four rendering gaps in `TreeLayout` by adding `PickerEntry.IconKey`, extracting two pure unit-testable helpers (`PickerTextHighlighter`, `PickerTreeBuilder`), refactoring `TreeLayout` to render from the tree model with leaf type icons, match-highlighting, folder icons, and scroll-into-view, and adding a demo scenario for visual verification.

---

## Task-by-Task

### Task 1.1 — `PickerEntry.IconKey`

- Added `string? IconKey = null` as a trailing optional parameter to the `PickerEntry` record.
- All existing 7-argument positional constructions keep compiling (verified: `S10_TypePicker`, `S12_AssetGridPicker`, `PickerWindow.OpenFromAdapter`, and tests still build).
- Documented via `<param>` XML doc.

### Task 1.2 — `PickerTextHighlighter.SplitRuns`

- **New file:** `src/NodeEditor.UI/Picker/PickerTextHighlighter.cs`
- Extracted the match-highlight chunking logic that was inline in `PickerItemListHelper.DrawRow` (lines ~137–167) into a pure, zero-ImGui `SplitRuns` method.
- `SplitRuns(name, matchPositions)` returns `IReadOnlyList<HighlightRun>` where each run has `Text` and `IsMatch`. Null/empty positions → single plain run. Empty name → empty list.
- **Refactored `PickerItemListHelper.DrawRow`** to call `SplitRuns` and iterate runs. The rendering output is identical — pure refactor, no visual change.

### Task 1.3 — `PickerTreeBuilder.Build`

- **New file:** `src/NodeEditor.UI/Picker/PickerTreeBuilder.cs`
- Extracted the inline folder/leaf grouping from `TreeLayout.DrawImplicitTree`/`DrawGroupedItems` into a pure, zero-ImGui `Build` method.
- `Build(items)` returns a `Node` tree: folders sorted `OrdinalIgnoreCase`, leaves in input order, `FullPath` on folder nodes, `FilteredIndex` on leaf nodes.
- Empty folders are naturally absent because the model is built only from the filtered input list.
- **Rewrote `TreeLayout.DrawImplicitTree`** to call `PickerTreeBuilder.Build` then render from the `Node` tree recursively via `DrawFolderNode`.

### Task 1.4 — TreeLayout rendering parity

Rewrote `TreeLayout.cs` completely:

1. **Folder icons:** `DrawFolderNode` resolves `ctx.Icons.TryGet("folder", ...)`; if found, draws `ImGui.Image` with the handle's UVs, then `ImGui.SameLine()` before `TreeNodeEx`. Unresolved → no icon, never throws.

2. **Leaf type icon:** `DrawLeafItem` resolves `re.Entry.IconKey` via `ctx.Icons.TryGet(...)`; if found, draws the icon image with UVs + padding. Unresolved/null → left-padded to align with icon-bearing rows. Never throws.

3. **Match highlight:** Calls `PickerTextHighlighter.SplitRuns` and renders each run with the appropriate color (matched = highlight, plain = default). Colors match `PickerItemListHelper`'s scheme.

4. **Invisible Selectable + draw-list text technique** (mirrors `PickerItemListHelper.DrawRow`): An invisible `Selectable("##sel")` with `AllowDoubleClick` captures hit-tests and double-click detection; the visual text runs are drawn via `ImGui.GetWindowDrawList().AddText()`.

5. **Scroll-into-view:** When `state.KeyboardFocusIndex == filteredIdx`, calls `ImGui.SetScrollHereY(0.5f)`.

6. All existing behaviors preserved: auto-focus search, clamp nav, auto-open-on-search (`DefaultOpen` when searching), hide-empty, folders non-selectable, double-click confirm, Enter/Esc, favorites/recent.

### Task 1.5 — Demo `S13_TreeIconPicker`

- **New file:** `src/NodeEditor.Demo/Scenarios/S13_TreeIconPicker.cs`
- Registered in `DemoShell.cs` between `S12_AssetGridPicker` and `S13_DebugVizMock`.
- Opens a `PickerLayout.Tree` picker with 8 entries across 5 category paths (`Blueprint/AI`, `Blueprint/Combat`, `HSM`, `BTree/Leaves`, uncategorized) with distinct `IconKey`s (`asset/blueprint`, `asset/hsm`, `asset/btree`, `asset/default`).
- Provides a nested `DemoIconProvider : IIconProvider` that returns distinct `IconHandle`s (with varying UVs) for those type keys and for `folder`/`folder_open`.
- **Wired via the same seam:** `fakeHost.PickerRegistry_.SetServices(iconProvider, fakeHost.Theme)` — the standard `PickerRegistry.SetServices` mechanism used by `FakeHostServices`.

---

## Extracted Helpers and How They're Consumed

| Helper | Extracted From | Now Used By |
|---|---|---|
| `PickerTextHighlighter.SplitRuns` | `PickerItemListHelper.DrawRow` (inline chunking) | `PickerItemListHelper.DrawRow`, `TreeLayout.DrawLeafItem` |
| `PickerTreeBuilder.Build` | `TreeLayout.DrawImplicitTree`/`DrawGroupedItems` (inline grouping) | `TreeLayout.DrawImplicitTree` → `DrawFolderNode` |

---

## ImGui Technique: Invisible Selectable + Draw-List Text

The `TreeLayout.DrawLeafItem` rendering uses the same technique as `PickerItemListHelper.DrawRow`:

1. An invisible `ImGui.Selectable("##sel", ...)` with `SpanAllColumns | AllowDoubleClick` captures all mouse hit-testing and keyboard interaction.
2. The visual name text (with match-highlighting) and type icon are drawn separately via `ImGui.GetWindowDrawList().AddText()` and `AddImage()` at precise screen positions.
3. Double-click detection via `ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(...)` confirms the selection.

This avoids the visual limitations of `ImGui.Selectable`'s built-in text rendering (no multi-color, no inline icon), while still getting correct click/focus/double-click behavior.

---

## How the Demo's Fake `IIconProvider` Is Wired

The seam is `PickerRegistry.SetServices(IIconProvider, IEditorTheme)`:
- `FakeHostServices` constructor calls `PickerRegistry_.SetServices(new FakeIconProvider(), new FakeEditorTheme())`.
- `S13_TreeIconPicker.DrawOverlay` calls `fakeHost.PickerRegistry_.SetServices(_iconProvider, fakeHost.Theme)` before opening the picker.
- `PickerWindow.BuildRenderContext()` picks up `_window.Icons` → feeds it to `TreeLayout` via `IPickerRenderContext.Icons`.

No new seams or parallel mechanisms were introduced.

---

## Test-Run Summaries

### NodeEditor.UI.Tests
```
Passed!  - Failed: 0, Passed: 51, Skipped: 0, Total: 51
```
Includes 5 new `PickerTextHighlighterTests` + 5 new `PickerTreeBuilderTests` + all existing picker tests still passing.

### NodeEditor.Core.Tests
```
Passed!  - Failed: 0, Passed: 181, Skipped: 0, Total: 181
```

### Build
```
Build succeeded. 0 Warning(s) 0 Error(s)
```
- `NodeEditor.sln` full build ✅
- `NodeEditor.Demo` with `TreatWarningsAsErrors` ✅
- Both test projects build ✅
- Zero new warnings anywhere.

---

## Edge Cases / Weak Points

1. **Demo icon textures are dummy IntPtrs:** The `DemoIconProvider` returns `IconHandle` with dummy `TextureId` values (1–6). ImGui will render white squares for unknown textures. This is sufficient for layout/UV verification but not a true visual icon test. A real atlas would be needed for production visual QA.

2. **TreeLayout icon alignment:** When no type icon is available, leaves are left-padded (`IconSize.X + 4f`) so text aligns with icon-bearing rows. This differs slightly from the flat `PickerItemListHelper` layout which centers text differently. It looks correct in the context of a tree with mixed icon/no-icon leaves.

3. **Explicit `CategoryNode` tree path unchanged:** `DrawExplicitTree` is left as-is (no folder icons, no highlight) because the batch instructions only scope the implicit-tree path. The explicit path is used by callers who supply their own `CategoryNode` structure.

4. **Case-insensitive folder grouping:** `PickerTreeBuilder` uses `OrdinalIgnoreCase` for both the `folderMap` dictionary and sorting. The folder `Name` field retains the casing of the *first* segment encountered, which matches the old `SortedDictionary` behavior (the first key's casing wins in the dictionary, but the display comes from the dictionary key which is the first-inserted casing). Note: the `Name` in the Node comes from the first segment's casing (because the folder is created on first encounter and not renamed), which differs from `SortedDictionary` where the key's casing depends on the first insertion. The test accounts for this by checking the folder exists (1 folder) and verifying sub-folders/leaves.

---

## Suggested Commit Message

```
feat(blueprints/picker): TreeLayout parity — type icons, match-highlight, folder icons, scroll

- Add PickerEntry.IconKey (trailing optional; 7-arg callers unaffected)
- Extract PickerTextHighlighter.SplitRuns (pure, used by ItemListHelper + TreeLayout)
- Extract PickerTreeBuilder.Build (pure, unit-testable tree model from Category paths)
- Rewrite TreeLayout to render from tree model with:
  - folder icons ("folder"/"folder_open" via ctx.Icons)
  - leaf type icons (via entry.IconKey)
  - fuzzy-match highlighting (via SplitRuns)
  - keyboard-focus scroll-into-view
  - invisible-Selectable + draw-list-text technique
- Add S13_TreeIconPicker demo scenario with fake IIconProvider
- Add 10 unit tests (5 PickerTextHighlighter + 5 PickerTreeBuilder)

Co-Authored-By: Claude <noreply@anthropic.com>
```

---

## Answer: ImGui Quirks with Leading Icon Alignment

Yes. The challenge is that `ImGui.TreeNodeEx()` / `ImGui.Selectable()` both manage their own text and hit-testing — you cannot inject an icon "inside" them. The solution: draw the icon **before** the ImGui widget, then rely on `ImGui.SameLine()` (for `TreeNodeEx`) or manual position advancement (for `Selectable`) to place the widget text at the correct offset.

For `TreeNodeEx` (folders), this works cleanly — `ImGui.Image` + `ImGui.SameLine()` + `ImGui.TreeNodeEx` renders the icon left-aligned with the expand arrow and the folder name to the right.

For `Selectable` (leaves), the invisible-`Selectable` + draw-list technique is needed because `ImGui.Selectable` with `SpanAllColumns` fills the full row width — you cannot put an icon "before" it. Instead, we let the invisible `Selectable` occupy the full row for hit-testing, then manually draw the icon and text at the correct X offsets. The text X advances by `IconSize.X + 4f` (or same padding when no icon, for alignment). This works but means the `CursorPosX` advancement is manual and must match between DrawList rendering and subsequent widgets — if a future change adds widgets after the leaf row, the cursor position would need to account for the drawn content width.
