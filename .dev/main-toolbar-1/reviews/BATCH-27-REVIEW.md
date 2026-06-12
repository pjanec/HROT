# BATCH-27 Review

**Batch:** BATCH-27 (MTB-P8-T1 — NodeEdit TreeLayout parity)
**Reviewer:** Development Lead
**Date:** 2026-06-12
**Status:** ✅ APPROVED

## Summary
NodeEdit `TreeLayout` brought to parity: leaf type icons, fuzzy match-highlight, folder icons, and
scroll-focused-leaf-into-view. Two pure helpers extracted (`PickerTextHighlighter.SplitRuns`,
`PickerTreeBuilder.Build`) and unit-tested; `PickerItemListHelper` refactored onto the shared splitter.
`PickerEntry.IconKey` added (DEC-14). Demo `S13_TreeIconPicker` added.

## Verification (independent, not trusting the report)
- Read every changed/new file. `SplitRuns` preserves the exact chunking semantics of the old inline code;
  `PickerItemListHelper.DrawRow` is a faithful pure refactor (same colors, no visual change).
- `PickerTreeBuilder.Build` is correct: nested folders, hide-empty (built only from the filtered list),
  uncategorized→root leaves, case-insensitive grouping + sort.
- Tests assert **actual structure/values** (run text+IsMatch, folder names, FullPath, FilteredIndex, leaf
  counts) — they would fail on a broken implementation. No `Assert.True(true)`, no `[Skip]`, no tautologies.
- Built `NodeEditor.sln`: **0 warnings, 0 errors** (Demo has TreatWarningsAsErrors).
- `NodeEditor.UI.Tests`: **51/51**; `NodeEditor.Core.Tests`: **181/181**. Run WITHOUT snapshot regen.

## Issues Found
1. **folder_open never used (named success condition)** — `DrawFolderNode` always requested `"folder"`;
   the design/condition names `folder`/`folder_open` (closed/open). Fixed in-review (trivial mechanical
   touch-up): read the node's persisted open-state from ImGui per-id storage and pick `folder_open` when
   open, falling back to `"folder"`. Re-verified build + tests green.

No other issues. NodeEdit stays generic (no Hrot/AssetKind refs); no scope creep; no deletions.

## 📝 Commit Message
```
feat(main-toolbar): NodeEdit TreeLayout parity — icons, match-highlight, folder icons, scroll (MTB-P8-T1)

Completes MTB-P8-T1 (Phase 8 BATCH-27).

Adopts NodeEdit's generic Tree picker for the upcoming Open-Asset UX by closing
its TreeLayout rendering gaps — generically, with no asset/editor specifics in NodeEdit.

- PickerEntry: add trailing optional `IconKey` (atlas-cell icon key resolved via
  IIconProvider; distinct from whole-texture IconTextureId). [DEC-14]
- PickerTextHighlighter.SplitRuns: pure match-highlight run splitter, now shared by
  both PickerItemListHelper (refactor, no visual change) and TreeLayout.
- PickerTreeBuilder.Build: pure Category→folder/leaf model (hide-empty, nested,
  case-insensitive); TreeLayout renders from it.
- TreeLayout: leaf type icons + match highlight + scroll-focused-leaf-into-view;
  folder/folder_open glyphs on folder nodes.
- Demo S13_TreeIconPicker (+ DemoShell registration) for runtime verification.

Tests: NodeEditor.UI.Tests 51/51 (10 new: PickerTextHighlighterTests×5,
PickerTreeBuilderTests×5), NodeEditor.Core.Tests 181/181. Build 0 warnings.

Related: .dev/main-toolbar-1/ASSET-PICKER-UX-DESIGN.md, TASK-DETAIL MTB-P8-T1, DEC-14

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```
