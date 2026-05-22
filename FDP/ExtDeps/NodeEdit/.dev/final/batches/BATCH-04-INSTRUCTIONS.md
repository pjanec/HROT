# BATCH-04: Panels and Picker

**Batch Number:** BATCH-04  
**Tasks:** TASK-P4-001, TASK-P4-002, TASK-P4-003  
**Phase:** Phase 4 — Panels, Picker (part 1)  
**Estimated Effort:** 10–14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01, BATCH-02, BATCH-03 (all completed)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch builds the three major side-surface components: the generic picker window (reused for 12+
contexts), the My Blueprint outline panel, and the Details inspector. All are pure ImGui UI in
`NodeEditor.UI`.

### Required Reading (IN ORDER)

1. **Ground Rules:** `.dev/final/instructions/00-START-HERE.md`
2. **Task Brief — Picker:** `.dev/final/instructions/03-06-task-picker.md` (read full file)
3. **Task Brief — My Blueprint + Details:** `.dev/final/instructions/03-07-task-myblueprint-details.md` (read full file)
4. **Spec Brief Part 2 §16–19:** `.dev/final/instructions/01-spec-brief-part2.md`
5. **Picker Spec:** `.dev/final/specs/C-picker.md` (normative)
6. **My Blueprint Spec:** `.dev/final/specs/D6-my-blueprint-panel.md` (normative)
7. **Previous Review:** `.dev/final/reviews/BATCH-03-REVIEW.md` (if it exists; skip if not)

### Source Code Location

- **Work area:** `src/NodeEditor.UI/Picker/`, `src/NodeEditor.UI/Panels/`
- **Core interfaces:** `src/NodeEditor.Core/Interfaces/` — do not modify

### Report Submission

**When done, submit your report to:** `.dev/final/reports/BATCH-04-REPORT.md`  
**Questions:** `.dev/final/questions/BATCH-04-QUESTIONS.md`

---

## Context

After this batch:
- A `PickerRegistry` and `PickerWindow` handle all 5 layouts (Standard/Compact/Wide/Grid/Tree) with
  fuzzy search, favorites, recent, keyboard navigation.
- `MyBlueprintPanel` renders the hierarchical asset outline with search, drag-source, and context menus.
- `DetailsPanel` dispatches to registered view providers and includes built-in fallback views.

---

## ⚠️ Critical Notes

### IPickerRegistry already in Core

`src/NodeEditor.Core/Interfaces/IPickerRegistry.cs` already exists (verbatim from kernel). Check
its method signatures before implementing `PickerRegistry`. The picker uses `IPickerRenderContext`,
`PickerSelectionMode`, and `PickerLayout` already defined there — do not redefine them.

### IMyBlueprintModel, IDetailsViewProvider already in Core

These are in `src/NodeEditor.Core/Interfaces/` from BATCH-01. Check the exact record shapes
before creating your model adapters.

### FuzzyMatcher API

```csharp
var result = FuzzyMatcher.Score(query, name, keywords);
// result.HasMatch — bool
// result.Score — int (higher = better)
// result.MatchPositions — IReadOnlyList<int> (char indices for highlight rendering)
```

### No new tests required

UI layer is verified through the demo (T-20). `dotnet test` must still pass all 59 existing tests.

---

## ✅ Tasks

### Task 1: Generic Picker Window (TASK-P4-001)

**Task Definition:** See `.dev/final/instructions/03-06-task-picker.md` (complete)  
**Normative spec:** `.dev/final/specs/C-picker.md`

**Files to create:**
```
src/NodeEditor.UI/Picker/PickerRegistry.cs
src/NodeEditor.UI/Picker/PickerWindow.cs
src/NodeEditor.UI/Picker/PickerRequest.cs
src/NodeEditor.UI/Picker/PickerResult.cs
src/NodeEditor.UI/Picker/PickerEntry.cs
src/NodeEditor.UI/Picker/PickerState.cs
src/NodeEditor.UI/Picker/FavoritesStore.cs
src/NodeEditor.UI/Picker/RecentStore.cs
src/NodeEditor.UI/Picker/Layouts/StandardLayout.cs
src/NodeEditor.UI/Picker/Layouts/CompactLayout.cs
src/NodeEditor.UI/Picker/Layouts/WideLayout.cs
src/NodeEditor.UI/Picker/Layouts/GridLayout.cs
src/NodeEditor.UI/Picker/Layouts/TreeLayout.cs
```

**Key requirements from the task brief + spec:**
- Search box focused on open; fuzzy-ranked results sorted by score desc
- Favorites + Recent sections pinned to top (regardless of query)
- Arrow/PgUp/PgDn/Home/End navigation; Enter confirms; ESC cancels
- Multi-select: Ctrl+click toggle, Shift+click range-select (if `AllowMultiSelect`)
- Right-click context menu: Favorite/Unfavorite, Copy ID
- Window closes on ESC, click outside, Enter confirm
- For >2000 entries: use `ImGuiListClipper`
- Default window size: 720×520; anchored at `AnchorScreen` if provided; clamped on-screen
- 5 layouts as described in task brief (Standard, Compact, Wide, Grid, Tree)

**Adaptation note:** The `PickerRequest` / `PickerEntry` types defined in the task brief are the
canonical shapes. If the `IPickerRegistry` in Core has slightly different signatures, adapt to match
Core (Core is ground truth).

---

### Task 2: My Blueprint Panel (TASK-P4-002)

**Task Definition:** See `.dev/final/instructions/03-07-task-myblueprint-details.md` T-15 section  
**Normative spec:** `.dev/final/specs/D6-my-blueprint-panel.md`

**Files to create:**
```
src/NodeEditor.UI/Panels/MyBlueprintPanel.cs
src/NodeEditor.UI/Panels/MyBlueprintItemRenderer.cs
src/NodeEditor.UI/Panels/MyBlueprintDragSource.cs
src/NodeEditor.UI/Panels/MyBlueprintContextMenu.cs
```

**Key requirements:**
- `Draw()` renders the full panel: header bar with [+ Add] popup, search box, sections in sort order
- Items grouped by CategoryPath (split on "/") into recursive folder tree
- Each row: selection highlight, 8px accent dot, 16×16 icon, name, badge chip, hover tooltip
- Search: FuzzyMatcher scoring, auto-expand matches, highlight matched chars, Esc restores state
- Drag source payload strings per item kind (see task brief table)
- Double-click → navigate (fire `navigateToGraph` / `navigateToItem` callbacks)
- Right-click context menu varies by item kind (rename, delete, duplicate, navigate, etc.)
- `SelectionChanged` event fired on single-click

---

### Task 3: Details Panel (TASK-P4-003)

**Task Definition:** See `.dev/final/instructions/03-07-task-myblueprint-details.md` T-16 section  
**Normative spec:** `.dev/final/specs/D7-details-panel.md` (if it exists in specs folder)

**Files to create:**
```
src/NodeEditor.UI/Panels/DetailsPanel.cs
src/NodeEditor.UI/Panels/DetailsViewRegistry.cs
src/NodeEditor.UI/Panels/Views/FallbackDetailsView.cs
src/NodeEditor.UI/Panels/Views/CommentDetailsView.cs
src/NodeEditor.UI/Panels/Views/MultipleNodesDetailsView.cs
```

**Key requirements:**
- `DetailsPanel.Draw()` looks up the best `IDetailsViewProvider` for the current `Target` type
- Built-in fallback: `FallbackDetailsView` renders a generic property-tree (key-value pairs from node properties)
- `CommentDetailsView` for when target is a comment
- `MultipleNodesDetailsView` for multi-selection: shows only shared properties
- `ShowAdvanced` toggle to reveal advanced sections

---

## 📊 Report Requirements

Submit `.dev/final/reports/BATCH-04-REPORT.md`:

```markdown
# BATCH-04 Report

## Tasks Completed
[TASK-P4-001 ✅/❌, TASK-P4-002 ✅/❌, TASK-P4-003 ✅/❌]

## Build & Test Results
[dotnet build: 0 errors 0 warnings? dotnet test: 59/59 still passing?]

## Developer Insights
1. Issues encountered:
2. Weak points spotted:
3. Design decisions beyond spec:

## Files Created
[list]
```

---

## ✅ Success Criteria

- [ ] `dotnet build` exits 0, 0 errors, 0 warnings
- [ ] `dotnet test` exits 0, all 59 previous tests pass
- [ ] `PickerRegistry.OpenPicker` and `PickerWindow.DrawFrame` exist and compile
- [ ] `MyBlueprintPanel.Draw()` exists and compiles
- [ ] `DetailsPanel.Draw()` exists and compiles
- [ ] `BATCH-04-REPORT.md` submitted
