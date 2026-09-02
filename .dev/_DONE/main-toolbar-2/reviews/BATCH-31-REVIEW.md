# BATCH-31 Review — MTB2-T2

**Status:** ✅ APPROVED (after in-review test-strengthening) · **Date:** 2026-06-12 · Reviewer: Dev Lead

## Verified (independent)
- Production correct: `ToolbarCommandAdapter.Register(..., ShellSaveCommands.SaveId, ..., sortOrder: -9)` added inside
  the null-safe `if (MainToolbar != null)` block, between Open Asset (-10) and `ToolbarSep_OpenAsset` (0).
  `shell.save` behavior/keybinding untouched; old blueprint save button kept.
- `SilkIconProvider`: `shell/save="g9"`, `shell/saveAs="h8"`, `shell/saveAll="i1"` — all previously-unused cells.
  Icon test `ShellSave_Icon_Resolves_DistinctCell` asserts resolve + distinct from all asset-kind + folder cells (real
  assertions).
- Build `Hrot.Editor` 0 warnings; `Hrot.Editor.AiShared.Tests` icon class 3/3; filtered `EditorSubsystemBlueprintWindows`
  13/13. No new Blueprints failures.

## Issue found + fixed (D-T2-1)
- The worker's guardrail asserted only `MainToolbar.Height > 0` — **does not prove the Save entry exists** (Height>0
  holds from other entries). Root cause: `GetVisibleItemPlan` is `internal`, not visible to `Hrot.Blueprints.Tests`.
- Lead fix (trivial, mechanical): added public `MainToolbarManager.ContainsEntry(string id)`; rewrote the guardrail to
  assert `ContainsEntry("shell.save")` **and** `ContainsEntry("shell.openAsset")`. Now fails if Save registration is
  removed. Re-verified green.

## Pending (lead runtime, non-blocking)
- Eyeball that the `g9` glyph reads as a save/disk icon; remap the cell if not (one-line, no correctness risk).

## Commit
`feat(main-toolbar2): Save icon in main toolbar + ContainsEntry guardrail (MTB2-T2)`
