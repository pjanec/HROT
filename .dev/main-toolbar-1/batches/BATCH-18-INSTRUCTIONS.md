# BATCH-18: BTree/HSM/Scenario INewAssetService impls + FolderTreePicker pick mode
**Tasks:** MTB-P6-T3, MTB-P6-T4   **Phase:** 6   **Est:** ~11h
**Dependencies:** BATCH-17 (`INewAssetService`, shared `RecipeMetadata`), BATCH-10 (`FolderTreePicker` read mode).

> Do T3 then T4 in sequence; do NOT advance until the current task's impl + tests pass.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §18.3 (per-kind minting), §18.1 (FolderTreePicker pick mode), §19 (scenario).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P6-T3, MTB-P6-T4.
4. Existing code (find via codebase-memory MCP + read):
   - `Hrot/Editor/Hrot.Editor.AiShared/Recipes/INewAssetService.cs` (BATCH-17) — `Kind`,
     `CreateNew(IEditableAsset? recipe, name, relPath)`, `AvailableRecipes()`.
   - `Hrot.AiEditor.Persistence.BTree.BTreeJsonServices.Serialize(dto)` and the BTree DTO/asset model
     (`Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`), plus how `EditorSubsystem.cs`
     (~L2216–2233) builds the DTO and writes BTree/HSM JSON (`saveBTreeDelegate`/`saveHsmDelegate`).
   - `Hrot.AiEditor.Persistence.Hsm.HsmJsonServices.Serialize(dto)` and the HSM asset/DTO model.
   - `Hrot/Subsystems/Hrot.Editor/IEditorLogic.cs` — `NewScenario`, `SaveScenarioAs`,
     `LoadScenarioByName`, `AvailableScenarios` (Scenario routing, §19).
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetRoots.cs` — `AssetsFor(kind)` (write base).
   - `Hrot/Editor/Hrot.Editor.AiShared/Browser/FolderTreePicker.cs` (read-mode tree builder, BATCH-10).

## DEV-LEAD DECISION (DEC-12 — read before coding)
Per §18.3, the **BTree/HSM/Scenario** `INewAssetService` impls **mint + persist** (write JSON / route
to `IEditorLogic`). The Blueprint impl (BATCH-17) is mint-only; the dialog (MTB-P6-T5) reconciles. So
in THIS batch the BTree/HSM impls write valid JSON under the Assets root, and the Scenario impl routes
to `IEditorLogic`.

## Task 1 — BTree / HSM / Scenario INewAssetService impls (MTB-P6-T3) — §18.3
Implement `INewAssetService` for each kind (place each in the matching editor assembly:
`Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, and Scenario in `Hrot.Editor`):
- **BTree** (`BTreeNewAssetService`, `Kind => BTree`): `CreateNew(recipe, name, relPath)` mints a fresh
  `AssetId` + minimal valid BTree (in-code "Empty") or clones a recipe BTree with new identity, then
  WRITES valid JSON via `BTreeJsonServices` under `AssetRoots.AssetsFor(BTree)/<relPath>/...` (mirror
  the `saveBTreeDelegate` DTO build in EditorSubsystem). `AvailableRecipes()` includes synthetic "Empty".
- **HSM** (`HsmNewAssetService`, `Kind => Hsm`): same pattern via `HsmJsonServices`.
- **Scenario** (`ScenarioNewAssetService`, `Kind => Scenario`): routes to `IEditorLogic` (§19) —
  **Empty** → `NewScenario()` (new empty world) then `SaveScenarioAs(relPath/name)`; **FromSeed** →
  load the seed (from `Recipes/Scenarios`) then `SaveScenarioAs` under the new name (fresh scenario at
  the new relpath). Use a narrow injected seam over `IEditorLogic` so it is unit-testable.

**Tests required:**
- `BTreeNewAssetTests.Create_WritesValidJson_UnderAssetsRoot_FreshId` — create under a temp Assets
  root; assert the JSON file exists at the relpath, has a fresh `AssetId`, and **round-trips**
  (deserialize back to an equal-enough asset).
- `HsmNewAssetTests.Create_WritesValidJson_FreshId` — analogous (round-trip).
- `ScenarioNewAssetTests.Create_Empty_NewWorld` — Empty path calls `NewScenario` then
  `SaveScenarioAs(relpath)` (assert via fake `IEditorLogic`); `_FromSeed_LoadsSeedThenSaveAs` — seed
  path calls load(seed) then `SaveScenarioAs(newName)`.

## Task 2 — FolderTreePicker pick mode (MTB-P6-T4) — §18.1
Extend `FolderTreePicker` (BATCH-10 added read mode) with **pick mode**: select an existing folder OR
add a new folder; yields a path **relative to the root**; bounded to the root (no escape). Keep the
logic separated from ImGui draw so it is unit-testable.
- A pick-mode model/state, e.g. `FolderPickerState` built from the same relative-path tree, with:
  `string SelectedRelPath` (relative to root, `/`-normalized, `""` = root); `AddFolder(parentRelPath,
  name) → newRelPath` (creates an in-model node, returns its relpath); selection of an existing folder
  sets `SelectedRelPath`.
- **Bounded to root:** reject `..` traversal / absolute paths / anything escaping the root — `AddFolder`
  and selection must never yield a path outside the root (sanitize names; reject `..`, `/`-leading,
  drive-letter, etc.).
- Folder icons via `folder`/`folder_open` keys in the draw path (no behavior assertion needed there).

**Tests required (`FolderTreePickerPickTests`):**
- `AddFolder_CreatesNode_ReturnsRelPath` — adding `"combat"` under root returns `"combat"` and the
  node appears; adding `"patrol"` under `"combat"` returns `"combat/patrol"`.
- `Selection_ReturnsRelPathRelativeToRoot` — selecting an existing folder yields its root-relative path.
- `CannotEscapeRoot` — `AddFolder` with a name containing `..` (or an absolute/`/`-leading path) is
  rejected/sanitized so the result never escapes the root (assert no `..` in any produced relpath).

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code. Keep `INewAssetService` (BATCH-17) intact
  (implement it; the Blueprint impl stays mint-only). Do NOT build the New Asset / Save-As dialogs
  (MTB-P6-T5/T6). No scope creep.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`,
  `Hrot.Editor.Tests`, + the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests` (PRE-3 EQS flake →
  re-run if it appears).
- Write `.dev/main-toolbar-1/reports/BATCH-18-REPORT.md`: files changed, each per-kind impl's
  mint+persist approach + the Scenario IEditorLogic seam, the pick-mode model + root-bounding, each new
  test + assertions, paste actual test-run summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
