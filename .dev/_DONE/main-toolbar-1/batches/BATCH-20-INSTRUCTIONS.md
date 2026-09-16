# BATCH-20: Save-As dialog (fresh-id duplicate semantics)
**Tasks:** MTB-P6-T6   **Phase:** 6   **Est:** ~6h. Completes Phase 6. Resolves DEC-9.
**Dependencies:** BATCH-17/18/19 (`INewAssetService`, `FolderPickerState`, `NewAssetDialog`,
`AssetSavePath`, collision guard), BATCH-06 (`ShellSaveCommands.requestSaveAs` seam, DEC-9).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §18.2 (Save-As dialog) + §18.5 (identity rule — fresh AssetId).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P6-T6.
4. Existing code (read):
   - `Hrot/Editor/Hrot.Editor.AiShared/Recipes/NewAssetDialog.cs` (BATCH-19) — mirror its model
     (collision guard, CreateNew, save, callback, DEC-12 per-kind save reconciliation).
   - `Hrot/Editor/Hrot.Editor.AiShared/Recipes/INewAssetService.cs` — `CreateNew(recipe, name, relPath)`.
   - `Hrot/Editor/Hrot.Editor.AiShared/AssetSavePath.cs`, `Browser/FolderTreePicker.cs`
     (`FolderPickerState`), `AssetBaseNameCollisionGuard`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs` (BATCH-06) — the
     `Action<AiDocument> requestSaveAs` seam (DEC-9) to be connected.

## Key rule (§18.5 — duplicate semantics)
Save-As ALWAYS mints a **fresh `AssetId`** (the catalog dedups by AssetId; a shared GUID would swallow
an asset). So Save-As over the current document = **clone-with-new-identity**, which is exactly
`INewAssetService.CreateNew(recipe: currentDocumentAsset, name, relPath)` (the current asset acts as the
"recipe" source). Reuse that path. Rename/move is NOT Save-As — do not touch the rename service.

## Scope — MTB-P6-T6
- **NEW** `Hrot/Editor/Hrot.Editor.AiShared/Recipes/SaveAsDialog.cs`: a thin dialog model (logic
  separated from ImGui) over the **current document's asset**:
  - Inputs: the current `IEditableAsset` (source content), **Name**, **`FolderPickerState`** (pick
    subfolder under the kind's Assets root), and the **collision guard**.
  - On **Confirm**: collision-check `name` at the picked relpath; if OK →
    `service.CreateNew(recipe: currentAsset, name, relPath)` (mints a **fresh AssetId** ≠ source) →
    save under `AssetsFor(kind)/<relpath>/<name>.<ext>` (DEC-12 per-kind reconciliation, same as
    NewAssetDialog: Blueprint via injected save delegate; BTree/HSM/Scenario persist in CreateNew) →
    invoke the caller callback with the new asset. For Scenario, route via `IEditorLogic.SaveScenarioAs`
    ("<relpath>/<name>") consistent with the ScenarioNewAssetService path.
  - Testable seams: `bool CanConfirm()`, `ConfirmResult Confirm(Action<IEditableAsset>? onCreated)`.
- **Resolve DEC-9:** connect `ShellSaveCommands`'s `requestSaveAs(AiDocument)` seam so that a `Save`
  with an empty `SourceFilePath` (and an explicit Save-As) opens/drives this `SaveAsDialog`. Provide
  the connection as a testable seam (e.g. the production `requestSaveAs` opens the dialog seeded from
  the document's asset). If full UI-surfacing of the dialog must wait for Phase 7 (consistent with
  DBT-2), wire the logical routing now and note any remaining UI-open glue under DBT-2. Update DEC-9's
  debt-tracker status accordingly.

## Tests required (`SaveAsDialogTests`, fakes + temp root)
- `SaveAs_WritesNewFile_WithFreshAssetId` — Save-As over a source asset writes a NEW file whose
  `AssetId` differs from the source's (duplicate semantics).
- `SaveAs_RespectsPickedRelPath` — the file is written under the picked relpath
  (`Assets/<Kind>/<relpath>/<name>.<ext>`).
- `CollisionGuard_RejectsExistingBaseName` — confirming a name that already exists at the relpath is
  rejected (no write), via the collision guard.
- `EmptySourcePathSave_RoutesToSaveAs` — cross-check with MTB-P2-T4: a `Save` whose active document has
  an empty `SourceFilePath` routes into the Save-As path (assert the `requestSaveAs`→Save-As connection
  fires; reuse/extend the BATCH-06 routing test as needed).

## Hard constraints
- Save-As MUST mint a fresh `AssetId` (never reuse the source's). Do NOT route rename/move through
  Save-As. REUSE `INewAssetService.CreateNew`, `AssetSavePath`, and `AssetBaseNameCollisionGuard`.
- Do NOT delete/modify legacy/assembly-loading code. No scope creep beyond the dialog + the DEC-9
  connection + tests.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.AiShared.Tests` + the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests`
  (PRE-3 EQS flake → re-run if it appears).
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-20-REPORT.md`: files changed, the fresh-id duplicate
  approach, the DEC-9 connection (and any residual glue → DBT-2), each new test + assertions, paste
  actual test-run summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
