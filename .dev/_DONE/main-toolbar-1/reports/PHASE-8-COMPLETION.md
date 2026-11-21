# Phase 8 — Asset Picker UX via NodeEdit's Picker (Tree layout) — COMPLETE

**Date:** 2026-06-12 · **Branch:** `blueprint-integ-1` · **Dev lead:** orchestrated via claude-worker-orchestrator (model `pro`)

All three Phase-8 tasks delivered, hard-reviewed, independently build+test-verified, and committed
one-batch-per-commit. The Open-Asset picker now reuses NodeEdit's generic Tree picker instead of the
old `AssetPickerModal`/`AssetBrowserPanel` modal; the docked browser is untouched.

| Task | Batch | Commit | Result |
|------|-------|--------|--------|
| MTB-P8-T1 — NodeEdit TreeLayout parity (icons, match-highlight, folder icons, scroll) | BATCH-27 | `1e7849d8` | NodeEditor.UI.Tests 51/51, Core.Tests 181/181; build 0 warnings |
| MTB-P8-T2 — AssetPickerSource + per-kind/folder icon distinctness (resolves DBT-1) | BATCH-28 | `40724723` | Hrot.Editor.AiShared.Tests 1033/1033; build 0 warnings |
| MTB-P8-T3 — wire Open-Asset picker via OpenPicker; retire AssetPickerModal path | BATCH-29 | `37f4f5d8` | Hrot.Editor.Tests 183/183; Blueprints.Tests 7 fail = PRE-1 only (zero new) |

## Design decisions made (recorded in DEBT-TRACKER)
- **DEC-14** — added generic `string? IconKey` to `PickerEntry` (atlas cells need a key resolved via
  `ctx.Icons`, not a whole-texture `IntPtr`). Corrects the design doc's "IconTextureId via ctx.Icons".
- **DEC-15** — opened via the entry-driven `PickerRegistry.OpenPicker(PickerRequest)` (which carries
  Category + icon), NOT the source-driven `registry.Open(sourceKey)` (which discards them). Avoided a
  ripple-y change to the generic `IPickerSource<TItem>` interface.

## Debt
- **DBT-1** resolved (testable part): icons now render in tree rows (T1 root-cause fix) and the 6 asset-kind
  cells + folder/folder_open are proven pairwise-distinct & resolvable (T2). Visual "recognizability" remains
  a runtime eyeball check once the picker is exercised live (per the debt's own caveat).

## Review notes (trust-but-verify catches)
- BATCH-28: worker reported "1025 passed" on a test file that **did not compile** (`Assert.Equal(int,int,string)`
  — no such xUnit overload). Caught by independent build; fixed in-review → 1033/1033.
- BATCH-27: worker omitted `folder_open` (only ever drew `folder`); added open/closed glyph selection in-review.

## Generic-vs-editor boundary held
All NodeEdit changes (T1) stayed asset-agnostic (no Hrot/AssetKind refs). Asset specifics live entirely
editor-side (`AssetPickerSource`, `AssetPickerLauncher`). Benefits every future NodeEdit Tree picker.

## Residual runtime check (not a code gate)
The picker's live behavior — folders, per-kind icons, keyboard nav, double-click open, Scenario→Load — should
be eyeballed in the running editor (the NodeEdit demo `S13_TreeIconPicker` exercises the rendering headlessly-
adjacent). No code gate remains for Phase 8.

**Phase 8 status: ✅ COMPLETE.** Every box in TASK-TRACKER Phase 8 is `[x]`.
