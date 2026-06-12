# BATCH-27: NodeEdit TreeLayout parity — type icons + match-highlight + folder icons + scroll

**Batch Number:** BATCH-27
**Tasks:** MTB-P8-T1 (NodeEdit `TreeLayout` parity)
**Phase:** Phase 8 — Asset Picker UX via NodeEdit's picker (Tree layout)
**Estimated Effort:** ~8h
**Priority:** HIGH (critical path; T2 and T3 depend on this)
**Dependencies:** none (pure NodeEdit framework work, no editor/Hrot deps)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Engineering rules (MUST follow):** `.dev/.guides/DEV-GUIDE.md`
2. **Design doc:** `.dev/main-toolbar-1/ASSET-PICKER-UX-DESIGN.md` — read **"The only code gaps (in NodeEdit `TreeLayout`)"** and **Decisions D1/D2**.
3. **Task definition:** `.dev/main-toolbar-1/TASK-DETAIL.md` → section **`MTB-P8-T1`** (the acceptance bar).

### Source Code Location (all paths relative to repo root)
- **TreeLayout to fix:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/Layouts/TreeLayout.cs`
- **Reference for highlight run-coloring (to extract):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerItemListHelper.cs` (the `DrawRow` match-highlight block, lines ~137–167)
- **Entry model:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerEntry.cs`
- **State model (Filtered/RankedEntry/MatchPositions/KeyboardFocusIndex):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerState.cs`
- **Icon resolution:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IIconProvider.cs` (`TryGet(key)` → `IconHandle` with `TextureId`, `Uv0`, `Uv1`)
- **Render context:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerWindow.cs` (`PickerRenderContext`, exposes `ctx.Icons`)
- **Grid layout icon-draw reference:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/Layouts/GridLayout.cs` (how `ImGui.Image` is used)
- **Existing Tree demo to mirror:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/Scenarios/S10_TypePicker.cs` + `S12_AssetGridPicker.cs`
- **Demo registration:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/DemoShell.cs` (~line 83–88, `_scenarios.Add(new S1x_…())`)
- **Test project:** `FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/` (xUnit + FluentAssertions; example: `Picker/PickerRegistryTests.cs`)
- **Solution:** `FDP/ExtDeps/NodeEdit/NodeEditor.sln`

### Report Submission
When done, write `.dev/main-toolbar-1/reports/BATCH-27-REPORT.md`.

---

## Context

NodeEdit already has a generic, virtualized, fuzzy, favorites/recent picker with a **Tree layout**
(`PickerLayout.Tree`) that groups leaves by each entry's `Category` path. We are **adopting** this picker
for the editor's Open-Asset UX (Phase 8). Before the asset source (T2) and editor wiring (T3) land, the
Tree layout has four small rendering gaps that must be closed **generically** (no asset specifics in
NodeEdit). This batch closes them and extracts two pure, unit-testable helpers.

**Two design clarifications already decided (see DEBT-TRACKER DEC-14, DEC-15) — implement exactly as below:**

- **DEC-14:** Per-kind icons are **atlas cells**: `IIconProvider.TryGet(key)` returns an `IconHandle` whose
  cell is a **UV sub-rect** (`Uv0`/`Uv1`) of one shared atlas texture. A bare `IconTextureId` (IntPtr,
  whole-texture) **cannot** address a cell. Therefore the leaf type-icon is driven by a **new
  `string? IconKey` field on `PickerEntry`**, resolved via `ctx.Icons.TryGet(IconKey)` and drawn with the
  handle's UVs. (`IconTextureId` stays as-is for Grid thumbnails.)

---

## ✅ Tasks

> Implement the tasks **in order**, building + running the NodeEditor.UI.Tests after each, per the
> MANDATORY WORKFLOW section below.

### Task 1.1 — Add `IconKey` to `PickerEntry`

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerEntry.cs` (UPDATE)

Add a **trailing optional** positional parameter so all existing 7-argument constructions keep compiling:

```csharp
public sealed record PickerEntry(
    string Id,
    string Name,
    string? Description,
    string? Category,
    IReadOnlyList<string>? Keywords,
    IntPtr? IconTextureId,
    object? Tag,
    string? IconKey = null);   // NEW — icon-provider key (atlas cell), resolved via ctx.Icons
```

Document `IconKey`: "Optional `IIconProvider` key resolved to an `IconHandle` (atlas cell) for inline
row icons in flat/tree layouts. Distinct from `IconTextureId` (whole-texture Grid thumbnails)."

**Constraint:** Do NOT change the order/types of the existing 7 params (the source-driven adapter in
`PickerWindow.OpenFromAdapter` and demos construct positionally with 7 args).

---

### Task 1.2 — Extract a pure match-highlight run splitter (shared by both layouts)

**File (NEW):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerTextHighlighter.cs`

The match-highlight chunking currently lives inline in `PickerItemListHelper.DrawRow`
(lines ~137–167). Extract the **pure** run-splitting (no ImGui) into a reusable helper:

```csharp
namespace NodeEditor.UI.Picker;

/// <summary>Splits a display name into alternating highlighted / plain runs
/// for fuzzy-match rendering. Pure (no ImGui) so it is unit-testable.</summary>
internal static class PickerTextHighlighter
{
    public readonly record struct HighlightRun(string Text, bool IsMatch);

    /// <summary>Split <paramref name="name"/> into consecutive runs where each run
    /// is either fully matched (positions in <paramref name="matchPositions"/>) or
    /// fully unmatched. Order preserved; concatenating Text yields the original name.
    /// Null/empty matchPositions ⇒ a single plain run (or empty list for empty name).</summary>
    public static IReadOnlyList<HighlightRun> SplitRuns(string name, IReadOnlyCollection<int>? matchPositions);
}
```

Requirements:
- Behavior must exactly preserve the existing chunking semantics in `PickerItemListHelper.DrawRow`
  (char index ∈ set ⇒ match). Concatenating all `Text` reproduces `name` exactly.
- Then **refactor `PickerItemListHelper.DrawRow`** to call `SplitRuns` and render each run (keep the
  existing colors/positioning: matched run = highlight color, plain run = default color). No visual
  change to the Standard/flat layout — this is a pure refactor of the same logic.

---

### Task 1.3 — Extract a pure tree-grouping model builder

**File (NEW):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerTreeBuilder.cs`

Today `TreeLayout` builds the folder/leaf hierarchy inline while rendering. Extract the **pure** grouping
(no ImGui) so it is unit-testable, then have `TreeLayout` render **from this model** (so test and render
never diverge):

```csharp
namespace NodeEditor.UI.Picker;

/// <summary>Pure builder that groups filtered picker entries into a folder/leaf tree
/// from each entry's Category path ("A/B/C"). Used by TreeLayout; unit-testable.</summary>
internal static class PickerTreeBuilder
{
    public sealed class Node
    {
        public string Name = "";                 // segment label (folder) ; leaf uses entry name
        public string FullPath = "";             // full category path of a folder ("A/B")
        public bool IsLeaf;
        public int FilteredIndex = -1;           // leaf: index into state.Filtered ; folder: -1
        public List<Node> Folders = new();       // child folders (sorted, OrdinalIgnoreCase)
        public List<Node> Leaves  = new();        // leaf children at this depth (in input order)
    }

    /// <summary>Build the root node. <paramref name="items"/> is the filtered list in display order;
    /// each item supplies its filtered index, Category (nullable), and Name.
    /// Folders are created only for categories that actually contain leaves (so empty/filtered-out
    /// folders are absent). Uncategorized entries become leaves directly under the root.</summary>
    public static Node Build(IReadOnlyList<(int FilteredIndex, string? Category, string Name)> items);
}
```

Requirements:
- Splitting on `'/'`, case-insensitive folder grouping (match the current `OrdinalIgnoreCase`
  `SortedDictionary` ordering), nesting to arbitrary depth — mirror the current
  `DrawImplicitTree`/`DrawGroupedItems` grouping rules.
- **Hide-empty:** because the model is built **only** from the filtered list, a folder appears iff it has
  at least one (possibly nested) leaf. No empty folders.
- An entry whose `Category` equals a folder's full path exactly is a **leaf at that folder's depth** (not
  a sub-folder) — preserve the existing "leaves vs sub-roots" split.

Then **rewrite `TreeLayout.Draw` (implicit-tree path) to render from `PickerTreeBuilder.Node`** recursively
(see Task 1.4). The explicit-`CategoryNode` path may remain as-is.

---

### Task 1.4 — TreeLayout rendering parity (icons + highlight + folder icons + scroll)

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/Layouts/TreeLayout.cs` (UPDATE)

Render from the `PickerTreeBuilder.Node` model. For each node:

1. **Folder node** → draw a folder icon then the `TreeNodeEx` header:
   - Resolve `ctx.Icons.TryGet(open ? "folder_open" : "folder", out var h)`. If found, draw
     `ImGui.Image(h.TextureId, new Vector2(16,16), h.Uv0, h.Uv1)` then `ImGui.SameLine()` before the
     `TreeNodeEx`. If not found, draw the header with no icon (never throw).
   - Keep current behavior: folders are **non-selectable** `TreeNodeEx` headers; **DefaultOpen when
     searching** (`!string.IsNullOrEmpty(state.SearchText)`).
2. **Leaf node** → in `DrawLeafItem`:
   - **Type icon:** if `re.Entry.IconKey` resolves via `ctx.Icons.TryGet(IconKey, out var h)`, draw
     `ImGui.Image(h.TextureId, new Vector2(16,16), h.Uv0, h.Uv1)` + `ImGui.SameLine()` before the name.
     If unresolved/null, render without an icon (alignment may be left-padded; do not throw).
   - **Match highlight:** render the leaf name using `PickerTextHighlighter.SplitRuns(re.Entry.Name,
     re.MatchPositions)` — matched runs in the highlight color, plain runs in the default text color
     (mirror `PickerItemListHelper`'s colors). The `Selectable` must remain (keep `AllowDoubleClick`,
     selection/focus, double-click → `state.Confirmed = true`) — render the colored text over/with an
     invisible or label-less `Selectable` so click + highlight both work (mirror the
     `PickerItemListHelper.DrawRow` invisible-selectable + draw-list-text technique).
   - **Scroll-into-view:** when `state.KeyboardFocusIndex == filteredIdx`, call `ImGui.SetScrollHereY(0.5f)`
     (same as `PickerItemListHelper` does at its line ~244).

**Keep generic — NO asset specifics in NodeEdit.** Do not reference Hrot/AssetKind/editor types anywhere
in NodeEditor.*. Folder keys are the literals `"folder"`/`"folder_open"`; type-icon keys come from
`entry.IconKey` (supplied by the caller).

Everything already working in Tree mode (auto-focus search, clamp nav, auto-open-on-search, hide-empty,
folders-non-selectable, dblclick, Enter/Esc, preview, recent/favorites) must **keep working**.

---

### Task 1.5 — Demo Tree scenario (runtime verification of icons + highlight + folder icons)

**File (NEW):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/Scenarios/S13_TreeIconPicker.cs`
**Register in:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/DemoShell.cs` (add `_scenarios.Add(new S13_TreeIconPicker());` next to the S10–S12 lines)

- Mirror `S10_TypePicker`: a `PickerLayout.Tree` picker opened via `fakeHost.PickerRegistry_.OpenPicker`.
- Items in **multiple categories** (e.g. `"Blueprint/AI"`, `"Blueprint/Combat"`, `"HSM"`, `"BTree/Leaves"`,
  and one uncategorized) — each `PickerEntry` sets a distinct **`IconKey`** (e.g. `"asset/blueprint"`,
  `"asset/hsm"`, `"asset/btree"` — any keys your demo icon provider knows).
- Provide a **fake `IIconProvider`** for the demo that returns distinct `IconHandle`s for those type keys
  **and** for `"folder"`/`"folder_open"`, so a human can visually confirm: type icons render on leaves,
  folder icons render on folders, and typing in the search box highlights the matched characters.
  (If the demo's `FakeHostServices`/registry already injects an `IIconProvider`, wire your fake one through
  the same seam used by `PickerRegistry.SetServices`. Inspect `DemoShell`/`FakeHostServices` to find it;
  do NOT invent a parallel mechanism.)
- The `TreatWarningsAsErrors` Demo project must build clean.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1.1–1.2:** implement → build `NodeEditor.UI` → add/run `PickerTextHighlighterTests` → **all pass** ✅
2. **Task 1.3:** implement → add/run `PickerTreeBuilderTests` → **all pass** ✅
3. **Task 1.4:** implement → build `NodeEditor.UI` → existing picker tests still pass ✅
4. **Task 1.5:** implement → build `NodeEditor.Demo` (warnings-as-errors) green ✅
5. Run the **entire** `NodeEditor.UI.Tests` + `NodeEditor.Core.Tests` suites green.

Do NOT stop to ask permission for obvious steps (running/fixing tests). Finish everything until green,
then write the report. No laziness; no stubbing to make the build pass.

---

## 🧪 Tests Required (exact names — these are the acceptance bar)

**File (NEW):** `FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/Picker/PickerTextHighlighterTests.cs`
- `SplitRuns_HighlightsMatchedRanges_ForGrdOverGuard` — name `"Guard"`, matchPositions `{0,3,4}` (fuzzy
  "grd") ⇒ exactly 3 runs: `("G", true)`, `("ua", false)`, `("rd", true)`. (Assert count, each Text, each
  IsMatch.)
- `SplitRuns_NoMatchPositions_YieldsSinglePlainRun` — name `"Guard"`, null (and separately empty) positions
  ⇒ one run `("Guard", false)`.
- `SplitRuns_AllMatched_YieldsSingleHighlightedRun` — positions `{0,1,2,3,4}` ⇒ one run `("Guard", true)`.
- `SplitRuns_ConcatenationReproducesName` — for a representative name + positions, `string.Concat(runs.Text)
  == name`.
- `SplitRuns_EmptyName_YieldsEmpty` — name `""` ⇒ empty list.

**File (NEW):** `FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/Picker/PickerTreeBuilderTests.cs`
- `Build_GroupsByCategoryPath_IntoNestedFolders` — items with categories
  `["Blueprint/AI","Blueprint/AI","HSM","Blueprint"]` ⇒ root has folders `Blueprint` (with sub-folder `AI`
  holding 2 leaves **and** 1 leaf directly under `Blueprint`) and `HSM` (1 leaf). Assert the folder names,
  nesting, `FullPath` values, and leaf counts/`FilteredIndex`es.
- `Build_OmitsEmptyFolders_DrivenByFilteredList` — only categories present in the (filtered) input produce
  folders; a category NOT in the input list produces no folder. (Pass a list missing a category and assert
  it's absent.)
- `Build_UncategorizedEntries_BecomeRootLeaves` — items with null/empty Category ⇒ leaves directly under
  root (no folder).
- `Build_FolderGrouping_IsCaseInsensitive` — `"AI/x"` and `"ai/y"` group under a single folder.
- `Build_LeafCountMatchesInput` — total leaves across the tree equals input item count.

> Tests must assert **actual structure/values** (run text+flags, folder names, FullPath, leaf indices),
> NOT just "not null" or "contains". No `Assert.True(true)`, no `[Skip]`, no asserting a mock you set up.

---

## 🎯 Success Criteria (from TASK-DETAIL MTB-P8-T1)

- [ ] `PickerEntry.IconKey` added (trailing optional; 7-arg callers unaffected).
- [ ] `PickerTextHighlighter.SplitRuns` extracted, used by **both** `PickerItemListHelper` and `TreeLayout`;
      its tests pass and prove correct runs for given `MatchPositions`.
- [ ] `PickerTreeBuilder.Build` extracted, used by `TreeLayout`; its tests prove the expected folder/leaf
      structure and **empty folders absent when filtered**.
- [ ] `TreeLayout` leaves draw the **type icon** (via `ctx.Icons.TryGet(entry.IconKey)`) + **match
      highlight**; folders draw **folder/folder_open icons**; the **keyboard-focused leaf scrolls into view**.
- [ ] `S13_TreeIconPicker` demo added + registered; renders icons + highlight + folder icons (runtime).
- [ ] `dotnet build` of `NodeEditor.sln` green; `NodeEditor.Demo` (warnings-as-errors) green.
- [ ] Full `NodeEditor.UI.Tests` + `NodeEditor.Core.Tests` green; existing picker tests still pass.

---

## ⚠️ Hard Constraints
- **No asset/editor specifics in NodeEdit** — NodeEditor.* must not reference Hrot/AssetKind. Folder keys
  are literal `"folder"`/`"folder_open"`; type icons come from `entry.IconKey`.
- **No scope creep** — only the files listed above. Do NOT change `IPickerSource<TItem>`, the adapter, or
  other layouts' behavior (CompactLayout/WideLayout/GridLayout). The `PickerItemListHelper` change is a
  **pure refactor** to use `SplitRuns` (no visual change).
- **No deletions** of existing functionality. Keep all existing Tree behaviors working.
- **Null-safe icons:** unresolved icon keys → render without an icon, never throw.
- No `TODO`/`NotImplementedException` in any path the success conditions cover. Zero new warnings.

---

## 📊 Report Requirements (`reports/BATCH-27-REPORT.md`)
- What you extracted (`SplitRuns`, `PickerTreeBuilder`) and how `TreeLayout`/`PickerItemListHelper` now
  consume them.
- The ImGui technique you used to draw icon + highlighted text alongside the `Selectable` in the tree leaf.
- How the demo's fake `IIconProvider` is wired (which seam).
- Test-run summaries (UI.Tests + Core.Tests counts, all pass) + demo build result.
- Any edge cases / weak points found; suggested commit message.
- Answer: did you hit any ImGui quirks aligning the leading icon with `TreeNodeEx`/`Selectable`?

If something cannot be done as specified, **stop and report why** rather than stubbing it.
