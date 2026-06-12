# BATCH-35 — MTB2-T6: `RecipePickerSource` (per-kind recipes incl. "Empty")

**Task:** MTB2-T6 (Item 1) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`
**Detail:** `.dev/main-toolbar-2/TASK-DETAIL.md` (`MTB2-T6`) · DECISIONS D-T6-1. **Mirrors Phase-8 `AssetPickerSource`.**

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file. 3. **Read the pattern to mirror:**
   `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerSource.cs` + its tests
   `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetPickerSourceTests.cs`.
4. `Hrot/Editor/Hrot.Editor.AiShared/Recipes/INewAssetService.cs` (`AvailableRecipes()` returns
   `IReadOnlyList<IEditableAsset>` **including the in-code "Empty" entry**; `CreateNew(...)`).

## ⚙️ RULES (non-negotiable)
1. Do this ONE objective only. Touch ONLY the files listed. No drive-by edits/renames.
2. NEVER hide a problem to pass a build (no excluded assets/`[Skip]`/weakened tests/stubs/suppression/`#if false`).
3. Add the EXACT named tests; assert real values; fail if code is wrong.
4. DO NOT STOP until build = 0 warnings AND the test command shows `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests + final summary. No litter.

## Objective
Project the per-kind recipes (from `INewAssetService.AvailableRecipes()`, including "Empty") into Tree-picker
`PickerEntry`s — the data seam for T7's new-from-recipe launcher. No production wiring in this batch.

## Scope — ONLY these files (NEW)
- `Hrot/Editor/Hrot.Editor.AiShared/Browser/RecipePickerSource.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/RecipePickerSourceTests.cs`

## Design (mirror `AssetPickerSource`, namespace `Hrot.Editor.AiShared.Browser`)
```csharp
public sealed record RecipeChoice(AssetKind Kind, IEditableAsset Recipe);

public sealed class RecipePickerSource : IPickerSource<RecipeChoice>
{
    public RecipePickerSource(
        IReadOnlyDictionary<AssetKind, Recipes.INewAssetService> services,
        Func<IEditableAsset, string?>? describe = null,        // recipe description; default null
        Func<IEditableAsset, string?>? recipeCategory = null); // optional sub-category; default null (D-T6-1)
    // PreferredLayout => Tree; SelectionMode => Single; Title "New Asset"; Cost Cheap; not async/drag.
}
```
Behavior:
- `Query(text, ctx)` → for each `(kind, service)` in `services` (iterate in a deterministic order — e.g. the
  dictionary's enumeration), for each `recipe` in `service.AvailableRecipes()`, yield `new RecipeChoice(kind, recipe)`;
  filter by `text` (case-insensitive `recipe.Name.Contains`). `QueryAsync` wraps it.
- `ToEntry(RecipeChoice rc)` (PUBLIC) → `PickerEntry`:
  - `Category` = `recipeCategory?.Invoke(rc.Recipe)` is non-empty `sub` ? `$"{rc.Kind}/{sub}"` : `rc.Kind.ToString()`.
  - `Name` = `rc.Recipe.Name` (the "Empty" entry's Name flows through unchanged).
  - `Description` = `describe?.Invoke(rc.Recipe)`.
  - `IconKey` = `AssetKindIcons.GetIconKey(rc.Kind)`; `Tag` = `rc`; `Keywords`/`IconTextureId` = null.
  - `Id` = `GetItemKey(rc)`.
- `BuildEntries(text, ctx)` (PUBLIC) = `Query(text, ctx).Select(ToEntry).ToList()`.
- `GetItemKey(rc)` = `$"{rc.Kind}:{rc.Recipe.Name}"` (stable across queries).
- `GetSearchableText(rc)` = `rc.Recipe.Name`.
- `RenderItem`/`RenderPreview`: minimal, guarded `if (ImGui.GetCurrentContext() != IntPtr.Zero) …` (mirror
  `AssetPickerSource`). `IsPreviewExpensive` → false; `CanAcceptDrop` → false.

## Tests — `RecipePickerSourceTests.cs` (EXACT names)
Use a fake `INewAssetService` whose `AvailableRecipes()` returns fake `IEditableAsset`s (one named `"Empty"` + one or
two named recipes), and a fake `IEditableAsset` (copy the `FakeEditableAsset` pattern from `AssetPickerSourceTests`).
- `Entries_IncludeEmptyPerKind` — build a source over `{ Blueprint: svcA, Hsm: svcB }` where each service's
  `AvailableRecipes()` includes an `"Empty"` recipe; `BuildEntries("")` contains an `"Empty"`-named entry for EACH
  kind (assert one per kind).
- `Entries_HaveKindCategory_PerKindIcon_AndRecipeTag` — a Blueprint recipe ⇒ `Category == "Blueprint"`,
  `IconKey == AssetKindIcons.GetIconKey(AssetKind.Blueprint)` (`"asset/blueprint"`), `Tag` is a `RecipeChoice` with
  `Kind == Blueprint` and the same recipe instance. With a `recipeCategory => "AI"`, `Category == "Blueprint/AI"`.
- `GetItemKey_StableAcrossQueries` — querying twice yields the same `GetItemKey` for the same recipe
  (`== "Blueprint:Empty"` etc.).
- `Description_FromRecipeMetadata_WhenPresent` — inject `describe = a => a == theRecipe ? "Clone of X" : null`;
  `ToEntry(...).Description == "Clone of X"` for that recipe, null for another.

> Assert actual values (Category strings, IconKey, Tag identity/kind, stable key, description). No tautologies/skips.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~RecipePickerSource"
```
Then a FULL `Hrot.Editor.AiShared.Tests` run should be green (lead will confirm). The filtered run must be `Failed: 0`.

## Definition of done
- `RecipePickerSource` + `RecipeChoice` added; `ToEntry`/`BuildEntries` public; per-kind recipes incl "Empty"
  projected with kind Category, per-kind IconKey, RecipeChoice Tag, optional description. No production wiring.
- The 4 named tests pass; build 0 warnings; filtered `RecipePickerSource` `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-35-REPORT.md`: shape, files changed, tests, final summary.

If something cannot be done as specified, STOP and report why.
