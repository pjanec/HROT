# MVE-BATCH-04 Review — editor Save (active blueprint → disk), projection-only
**Status:** ✅ APPROVED (after rewire)   **Date:** 2026-06-03

## Summary
Save the opened blueprint to its `.bp.json` with pins cleared (projection-only). Completes the kernel+button+save slice.

## Lead-caught issue (fixed)
The initial sonnet pass wired Ctrl+S/Save into **`GraphEditorWindow`** — the orphaned legacy window (zero refs from `EditorSubsystem`), so Save was dead in the real editor. A second sonnet pass rewired it into `EditorSubsystem` (mirroring the MVE-03 run-button) and resolved via the active document. It over-reverted (removed a pre-existing no-op Save button from the legacy window); the lead reverted `GraphEditorWindow.cs` fully to HEAD so MVE-04 touches only the real editor.

## Verification (sonnet self-verify + lead spot-check)
- `Hrot.Blueprints.Editor` build **0/0** after the lead's GraphEditorWindow revert (the Save core/wiring don't depend on the legacy window). Solution build was 0/0 pre-revert; the revert only restores original legacy code.
- `SaveActiveBlueprintCommandTests` **8/8**; `EditorSubsystemBoot` **10/10** (Save command/button registered at composition, still boots); `Hrot.Blueprints.Tests` **1147/10** (DEBT-006; incl. the existing byte-stability test); `Hrot.Editor.AiShared.Tests` **761/0**.

## Code read (lead)
- `SaveActiveBlueprintCommand.Save(asset, path)` clears each node's `Pins` on a temp swap and **restores in a `finally`** (live asset's pins untouched) → `BlueprintJsonServices.Serialize` → `File.WriteAllText`. Projection-only preserved (saved `Pins:[]`).
- `SaveFromActiveDocument(AiDocumentManager, DirtyTracker, report)` resolves `Active.ViewState as AiCanvasContext → AssetRef as BlueprintAsset` + `Active.Asset.SourceFilePath`, then `MarkClean` on both the doc and the dirty tracker. Correct real-editor path (no `GraphEditorWindow`).
- Wired in `EditorSubsystem`: "Save Blueprint" toolbar entry (CaptureWindowRegistrar) + DrawUI button + Ctrl+S, gated on `!_headless && ImGui context`. Clear status surfaced.

## Verdict
APPROVED. Save works from the real editor; projection-only respected; tests green; boot unaffected. The vertical slice (kernel run + run-button + save) is complete. Remaining MVE: compile-on-demand (so the run-button resolves arbitrary opened/uncompiled blueprints), hot-reload, debug-observe.

## Commit Message
```
feat(blueprint-mve): editor Save for the opened blueprint (MVE-BATCH-04)

SaveActiveBlueprintCommand: projection-only Save — clears each node's Pins on a temp swap (restored in
finally so the live canvas asset is never mutated) before BlueprintJsonServices.Serialize → File.WriteAllText,
so saved .bp.json stay Pins:[] (links keep their GUIDs; pins rehydrate on load). SaveFromActiveDocument
resolves the active doc (AiDocumentManager.Active → AiCanvasContext.AssetRef + BlueprintFileAsset.SourceFilePath)
and marks clean. Wired in EditorSubsystem: "Save Blueprint" toolbar entry + DrawUI button + Ctrl+S (gated on
ImGui context) — the REAL editor (not the orphaned GraphEditorWindow).

Build 0/0. Save tests 8/8; EditorSubsystemBoot 10/10; Blueprints 1147/10 (DEBT-006, incl. byte-stability);
AiShared 761/0.
```
