# BATCH-17 Report

## Implementation Summary

**T1 — Shared `RecipeMetadata` (MTB-P6-T1):** Created a new kind-agnostic `RecipeMetadata`
type in `Hrot.Editor.AiShared/Recipes/` with the five fields: `DisplayName`, `Category`,
`Description`, `Difficulty` (default `"Beginner"`), and `ConceptsTaught` (`List<string>`,
never null). Added a `RecipeMetadataAdapter.ToShared()` extension method in
`Hrot.Blueprints.Editor` that maps the Compiler JSON-model `RecipeMetadata` (netstandard2.0)
to the shared type (net8.0). The Compiler type and source generator are untouched.

**T2 — `INewAssetService` + Blueprint impl + "Empty" (MTB-P6-T2):** Defined
`INewAssetService` in `Hrot.Editor.AiShared/Recipes/` with `Kind`, `CreateNew(recipe?, name,
relPath) → IEditableAsset`, and `AvailableRecipes()`. Implemented `BlueprintNewAssetService`
in `Hrot.Blueprints.Editor`:
- Real recipes → `NewFromRecipeService.CreateFromRecipe` (clone + fresh AssetId + strip
  recipe metadata).
- `null` / "Empty" → synthesizes a minimal valid `BlueprintAsset` in code (no disk read,
  no file I/O). The in-code "Empty" appears as a synthetic recipe in `AvailableRecipes()`.
- All results are returned as `IEditableAsset` via `BlueprintEditableAssetAdapter`.

## Design Decisions

1. **Shared RecipeMetadata in AiShared, not a neutral assembly.** The DEC-11 note ruled out
   moving the Compiler type and there's no netstandard2.0-compatible shared assembly available
   for editor infra. The shared `RecipeMetadata` lives in `Hrot.Editor.AiShared` (net8.0 only)
   and the Compiler type remains in `Hrot.Blueprints.Compiler` (netstandard2.0 multi-target).
   The mapping adapter bridges them where the blueprint editor (net8.0) needs to surface shared
   metadata.

2. **Extension method for mapping.** The `RecipeMetadataAdapter.ToShared()` is an extension
   method on the Compiler `RecipeMetadata` so the mapping reads naturally as
   `compilerMeta.ToShared()`. It produces a new copy — modifying the shared result never
   affects the Compiler original.

3. **Empty recipe via adapter pattern.** The "Empty" recipe is a `BlueprintEditableAssetAdapter`
   wrapping a minimal `BlueprintAsset` with `EditorMetadata.Recipe` metadata (so it shows as a
   recipe in `AvailableRecipes()`). `CreateNew` uses a separate instance that has no recipe
   metadata — matching the semantics of `NewFromRecipeService` which strips recipe metadata from
   clones.

4. **`BlueprintEditableAssetAdapter` reuse.** Rather than inventing a new `IEditableAsset`
   wrapper, `BlueprintNewAssetService` uses the existing `BlueprintEditableAssetAdapter` from
   `Hrot.Blueprints.Editor.Variables` (which already wraps `BlueprintAsset` with
   `SourceFilePath = ""`, `IsDirty = false`, etc.). Tests cast via `IsType<T>()` to access the
   underlying `BlueprintAsset` for content assertions.

5. **No file I/O.** `CreateNew` mints identity and in-memory content only. `SourceFilePath`
   on the returned asset is empty — file writing is deferred to MTB-P6-T5/T7 (`NewAssetDialog`
   and subfolder-aware save).

## Deviations

- **Mapping helper location:** The batch says "in the blueprint editor or a shared adapter."
  Placed it in `Hrot.Blueprints.Editor` (the only assembly that references both
  `Hrot.Blueprints.Compiler` and `Hrot.Editor.AiShared`). No shared adapter assembly exists
  that can reference both — this is the correct home.

## Test Results

### Hrot.Editor.AiShared.Tests (Stability filter)
```
Passed!  - Failed:     0, Passed:   952, Skipped:     0, Total:   952
```

### Hrot.Editor.AiShared.Tests — RecipeMetadataTests (unfiltered)
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5
```
- `SharedType_HasAllFields` — sets all 5 fields, reads back, asserts exact values
- `Defaults_Difficulty_IsBeginner` — default `Difficulty` == `"Beginner"`
- `Defaults_ConceptsTaught_IsEmptyNotNull` — `ConceptsTaught` is empty list, not null
- `Defaults_DisplayName_IsEmpty` — default `DisplayName` == `""`
- `SetAllFields_ThenReadBack_AllMatch` — round-trip of all fields, same-reference on list

### Hrot.Blueprints.Tests — New tests + existing recipe tests (class filter)
```
Passed!  - Failed:     0, Passed:    49, Skipped:     0, Total:    49
```
Includes:
- **`RecipeMetadataAdapterTests` (4 tests):** `ToShared_MapsAllFields`,
  `ToShared_NullInput_ReturnsNull`, `ToShared_DefaultValues_Preserved`,
  `ToShared_ModifyingCopy_DoesNotAffectOriginal`
- **`NewAssetServiceTests` (8 tests):** `CreateNew_MintsFreshAssetId`,
  `CreateNew_MintsDifferentIdThanRecipe`, `Empty_ProducesMinimalValidBlueprint_InCode`,
  `Empty_ProducesMinimalValidBlueprint_WithEmptySentinel`,
  `CreateNew_FromRecipe_ClonesContent_NewIdentity`, `AvailableRecipes_IncludesEmptyEntry`,
  `CreateNew_NullName_Throws`, `Kind_IsBlueprint`
- **Existing `NewFromRecipeServiceTests` (7 tests):** All pass unchanged
- **Existing `RecipeIntegrityTests` (30 tests):** All pass unchanged

### Fdp.Toolkits.Tests (Stability filter)
```
Passed!  - Failed:     0, Passed:  1856, Skipped:     0, Total:  1856
```

### Hrot.SimHost.Tests (Stability filter)
```
Passed!  - Failed:     0, Passed:   585, Skipped:     3, Total:   588
```
3 skipped are pre-existing (`SimHostSubsystem_InitializeHeadless_DoesNotThrow`,
`CgfSubsystem_InitializeHeadless_DoesNotThrow`,
`OnLoad_RegistersFireInteractionEventTranslator`).

### Full solution build
```
Build succeeded.  0 Warning(s)  0 Error(s)
```
Includes the netstandard2.0 generator target (`Hrot.Blueprints.Compiler`).

## Developer Insights

- **No issues encountered.** The implementation was straightforward — the existing
  `NewFromRecipeService` and `BlueprintEditableAssetAdapter` provided the right patterns
  to follow.
- **Namespace collision risk:** Having two types named `RecipeMetadata` (Compiler and
  shared) required fully-qualified names in the adapter. A future refactor could rename
  the Compiler type or unify them behind an interface, but the DEC-11 decision keeps them
  separate for now.
- **Empty synthesis reuses `MakeEmptyBlueprint()`.** The constructor adds recipe metadata
  to the shared `_emptyBlueprint` for `AvailableRecipes()`, while `CreateNew(null/empty)`
  returns fresh instances without recipe metadata. This mirrors `NewFromRecipeService`'s
  strip semantics.

## Known Issues

None. The "Empty" blueprint is minimal — it has no graphs, no variables, no event
dispatchers. This is valid JSON and will deserialize without errors. Adding a default
"Main" graph would be a content decision for a later recipe-authoring phase.

## Suggested Commit Message

```
feat(main-toolbar): shared RecipeMetadata + INewAssetService + Blueprint impl with "Empty" (MTB-P6-T1, T2)
```
