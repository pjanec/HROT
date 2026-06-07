# MVE-BATCH-03 Review — toolbar "Run Opened Blueprint on Selected Entity"
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
Toolbar button that attaches the opened Instance Blueprint to the currently-selected entity via the MVE-02 `BlueprintAttachService`. Run-mode-agnostic. Implemented + self-verified by a **sonnet** agent (per the cost directive); lead reviewed the report + read the core command.

## Verification (sonnet agent, lead-reviewed)
- Build **0 errors / 0 warnings** (touched projects). New `RunBlueprintOnEntityCommandTests` **7/7**; `EditorSubsystemBoot` **10/10** (composition still boots with the button registered); `Hrot.Blueprints.Tests` **1138 / 11** (10 DEBT-006 + flaky perf — no new); `Hrot.Editor.AiShared.Tests` **761/0**.

## Code read (lead)
- `RunBlueprintOnEntityCommand.Execute(world, registry, selectedEntity, activeAssetRef, report)` — pure, ImGui-free, reuses `BlueprintAttachService`; graceful no-op + message for each precondition (no world/registry/entity/asset, not-a-blueprint) and each `BlueprintAttachStatus` (Attached/AlreadyAttached/NotRegistered→"compile first"/NotInstanceKind/NoSlotAvailable). Clean.
- Correctly disambiguated the two `EditorSelectionStore` classes: used the **AiShared** one (`SelectedEntity`, EditorSubsystem `_aiEditorSelectionStore`); active asset via `_aiDocumentManager.Active?.ViewState as AiCanvasContext → AssetRef as BlueprintAsset`. ImGui button in `DrawUI` gated on context. `CaptureWindowRegistrar` (Internal) makes `RegisterToolbarEntry` headless-testable.

## Verdict
APPROVED. Manual run-button is in (attach-only, run-mode-agnostic), reusing the verified attach seam; boot unaffected. The opened blueprint must be registered (compiled) for attach to resolve — on-demand compile is a later MVE step (the button reports "compile/register first" otherwise). Next: MVE-04 (Save).

## Commit Message
```
feat(blueprint-mve): toolbar "Run Opened Blueprint on Selected Entity" button (MVE-BATCH-03)

RunBlueprintOnEntityCommand.Execute (headless-testable, ImGui-free) resolves the AiShared
EditorSelectionStore.SelectedEntity + the active AiCanvasContext.AssetRef blueprint and attaches it via
BlueprintAttachService (idempotent, run-mode-agnostic). EditorSubsystem registers a toolbar entry
(IWindowRegistrar.RegisterToolbarEntry) + a DrawUI button gated on the ImGui context; status surfaced
(Attached/AlreadyAttached/NotRegistered/NotInstanceKind/NoSlotAvailable). CaptureWindowRegistrar makes
registration headless-testable.

Build 0/0. New command tests 7/7; EditorSubsystemBoot 10/10; Blueprints 1138/10 (DEBT-006); AiShared 761/0.
```
