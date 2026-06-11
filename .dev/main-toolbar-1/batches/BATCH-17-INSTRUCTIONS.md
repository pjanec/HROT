# BATCH-17: Shared RecipeMetadata + INewAssetService (+ Blueprint impl, "Empty")
**Tasks:** MTB-P6-T1, MTB-P6-T2   **Phase:** 6 — Unified Creation & Recipes   **Est:** ~9h
**Dependencies:** Phase 5. T2 uses T1's shared `RecipeMetadata`.

> Do T1 then T2 in sequence; do NOT advance until the current task's impl + tests pass.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §17 (recipe model; in-code "Empty") + §18.3 (per-kind minting).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P6-T1, MTB-P6-T2.
4. Existing code (read):
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/GraphTypes.cs` — the EXISTING
     `RecipeMetadata` (DisplayName/Category/Description/Difficulty/ConceptsTaught) on the blueprint
     JSON model (`EditorMetadata.Recipe`). **This assembly multi-targets netstandard2.0 (the source
     generator) — it must NOT reference net8.0 `Hrot.Editor.AiShared`.**
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NewFromRecipeService.cs` —
     `CreateFromRecipe(BlueprintAsset recipe, string newName)` (already clones + mints `Guid.NewGuid()`).
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/RecipeCreateModal.cs` — reads
     `recipe.EditorMetadata.Recipe` (Compiler `RecipeMetadata`).
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs`, `AssetKind.cs`.

## DEV-LEAD DECISION (DEC-11 — read before coding)
§17 says "lift RecipeMetadata into shared editor infra." Code reality: the blueprint
`RecipeMetadata` is on the netstandard2.0 Compiler JSON model and CANNOT move to net8.0 AiShared
(no neutral shared assembly exists). So:
- **Define a NEW shared `RecipeMetadata`** (same fields) in **`Hrot.Editor.AiShared`** (net8.0) — the
  kind-agnostic type the unified creation flow (INewAssetService, dialogs, BTree/HSM/Scenario) uses.
- **Keep** the Compiler JSON-model `RecipeMetadata` as-is (serialization/generator).
- The blueprint **editor** code (net8.0) maps Compiler `RecipeMetadata` → shared `RecipeMetadata`
  where the unified flow needs it (a small mapping helper). Do NOT touch the Compiler model's type or
  the netstandard2.0 generator.

## Task 1 — Shared `RecipeMetadata` (MTB-P6-T1) — §17
- **NEW** `Hrot/Editor/Hrot.Editor.AiShared/Recipes/RecipeMetadata.cs`: a shared type with the fields
  `DisplayName`, `Category`, `Description`, `Difficulty` (default `"Beginner"`), `ConceptsTaught`
  (`List<string>`/`IReadOnlyList<string>`). Document it as the kind-agnostic recipe metadata.
- Add a small mapping helper (net8.0, in the blueprint editor or a shared adapter) converting the
  Compiler `RecipeMetadata` → the shared one, so blueprint recipe code can surface shared metadata.
  Existing blueprint recipe discovery/tests must keep passing.

**Tests required (`RecipeMetadataTests`, in `Hrot.Editor.AiShared.Tests`):**
- `SharedType_HasAllFields` — instantiate the shared `RecipeMetadata`, set & read all five fields,
  assert defaults (`Difficulty == "Beginner"`, `ConceptsTaught` empty-not-null).
- (If you add the mapping helper) a test that maps a Compiler `RecipeMetadata` → shared and asserts
  field-by-field equality. Existing blueprint recipe tests must still pass.

## Task 2 — `INewAssetService` + Blueprint impl + in-code "Empty" (MTB-P6-T2) — §17, §18.3
- **NEW** `Hrot/Editor/Hrot.Editor.AiShared/Recipes/INewAssetService.cs`:
  ```csharp
  public interface INewAssetService {
      AssetKind Kind { get; }
      // recipe == null → the in-code "Empty" recipe. relPath = target subfolder under the kind root.
      IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath);
      // The recipes this kind offers (the in-code "Empty" + any discovered recipe assets).
      IReadOnlyList<IEditableAsset> AvailableRecipes();  // include a synthetic "Empty" entry
  }
  ```
  (You may refine the exact shape, but keep: fresh `AssetId` minting, a `null`/sentinel = in-code
  "Empty", and the returned `IEditableAsset` carries the new identity + name. Document any change.)
- **NEW** Blueprint impl in `Hrot.Blueprints.Editor` (e.g. `BlueprintNewAssetService`):
  - `Kind => AssetKind.Blueprint`.
  - `CreateNew(recipe, name, relPath)`: when `recipe` is a real blueprint recipe → wrap
    `NewFromRecipeService.CreateFromRecipe(recipeBlueprint, name)` (clones content, mints fresh
    `AssetId`); when `recipe` is the in-code "Empty" → synthesize a **minimal valid blueprint in code**
    (NO disk read, NO on-disk JSON) with a fresh `AssetId` and the given name. Return it as
    `IEditableAsset`. (This batch does NOT write to disk — the dialog/save batches do that; CreateNew
    mints the in-memory asset with identity. If your impl needs a path for the asset, set it from
    `relPath` but do not perform file I/O here unless a test requires it — keep CreateNew pure-mint.)
  - The in-code "Empty" appears in `AvailableRecipes()` as a synthetic recipe named "Empty".

**Tests required (`NewAssetServiceTests`, in the blueprint editor test project):**
- `CreateNew_MintsFreshAssetId` — the returned asset's `AssetId` is non-empty and differs from any
  source recipe's id (and differs across two calls).
- `Empty_ProducesMinimalValidBlueprint_InCode` — `CreateNew(null/"Empty", name, relPath)` yields a
  minimal VALID blueprint built in code with NO disk read (assert no file access — e.g. it works with
  no recipe files present / a temp-empty root), correct name, fresh id.
- `CreateNew_FromRecipe_ClonesContent_NewIdentity` — from a real recipe, the new asset has the
  recipe's content but a DIFFERENT `AssetId` (clone-with-new-identity).

## Hard constraints
- Do NOT touch the Compiler `RecipeMetadata` type or the netstandard2.0 source generator. Do NOT move
  the Compiler model type. Do NOT delete/modify legacy/assembly code. No scope creep beyond T1/T2.
- `CreateNew` mints in-memory (identity + content); file writing is MTB-P6-T5/T7 — do NOT add it here.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings) — incl. the netstandard2.0 generator target.
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.AiShared.Tests` + the blueprint editor test project + the hot suites
  `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests` (PRE-3 EQS flake → re-run). For `Hrot.Blueprints.Tests`
  run new tests by class filter (PRE-1 pre-existing failures; do NOT touch them).
- Write `.dev/main-toolbar-1/reports/BATCH-17-REPORT.md`: files changed, the shared-vs-Compiler
  RecipeMetadata split + mapping, the INewAssetService shape + "Empty" synthesis, each new test +
  assertions, paste actual test-run summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
