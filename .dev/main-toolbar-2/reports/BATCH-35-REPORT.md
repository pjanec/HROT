# BATCH-35 Report

**Batch:** BATCH-35 — MTB2-T6: `RecipePickerSource` (per-kind recipes incl. "Empty")
**Developer:** pjanec (Claude)
**Date:** 2026-06-12
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| MTB2-T6 (Item 1) — `RecipeChoice` record | ✅ | `sealed record RecipeChoice(AssetKind Kind, IEditableAsset Recipe)` in `Hrot.Editor.AiShared.Browser` |
| MTB2-T6 (Item 2) — `RecipePickerSource` class | ✅ | Implements `IPickerSource<RecipeChoice>`, mirrors `AssetPickerSource` pattern |
| MTB2-T6 (Test 1) — `Entries_IncludeEmptyPerKind` | ✅ | Asserts "Empty"-named entries per kind from two services |
| MTB2-T6 (Test 2) — `Entries_HaveKindCategory_PerKindIcon_AndRecipeTag` | ✅ | Asserts Category, IconKey, Tag shape; tests recipeCategory sub-grouping |
| MTB2-T6 (Test 3) — `GetItemKey_StableAcrossQueries` | ✅ | Stable `"Blueprint:Empty"` key across two queries |
| MTB2-T6 (Test 4) — `Description_FromRecipeMetadata_WhenPresent` | ✅ | `describe` lambda injects "Clone of X" for one recipe, null for another |

---

## 🧪 Testing Results

**Unit Tests Passed:** 4 / 4
**Integration Tests Passed:** N/A (no production wiring in this batch)

```
dotnet test Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~RecipePickerSource"
Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 19 ms
```

**Key Test Scenarios Verified:**
- [x] Two kinds (Blueprint, Hsm), each with "Empty" → two "Empty" entries, distinct RecipeChoice.Kind
- [x] Category = "Blueprint" (plain kind), IconKey = "asset/blueprint", Tag is RecipeChoice with correct Kind+Recipe
- [x] With `recipeCategory => "AI"`, Category = "Blueprint/AI"
- [x] `GetItemKey` returns `"Blueprint:Empty"` identically across queries
- [x] `describe` lambda selectively sets description ("Clone of X") for one recipe, null for another

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

No issues. The pattern to mirror (`AssetPickerSource`) was clear and well-structured. The key design
decisions were already made in D-T6-1 — the RecipePickerSource mirrors AssetPickerSource's public
`ToEntry`/`BuildEntries` seam, with `IPickerSource<RecipeChoice>` as the generic parameter instead of
`IPickerSource<IEditableAsset>`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

None relevant to this batch. The `AssetPickerSource` pattern is solid and the mirroring was
straightforward.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **Deterministic iteration order**: The instructions say "iterate in a deterministic order — e.g. the
  dictionary's enumeration". I chose `services.Keys.OrderBy(k => k)` (enum declaration order) rather
  than relying on `Dictionary` enumeration order (which is insertion order in modern .NET, but
  `OrderBy` makes the intent explicit and is immune to dictionary implementation changes).
- **`Enumerable.Select` in `BuildEntries`**: Uses `Query(…).Select(ToEntry).ToList().AsReadOnly()`,
  mirroring `AssetPickerSource.BuildEntries` exactly.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **Empty service dictionary**: If no services are registered, `Query` returns an empty list (the
  `_kinds` list is empty, no iteration happens). This is correct behavior — no crashes.
- **Service missing for a kind in `_kinds`**: Guarded by `TryGetValue` — skip silently.
- **`recipeCategory` returning empty string vs null**: Both treated as "no sub-category" via
  `!string.IsNullOrEmpty(sub)`.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

No performance concerns. The `RecipePickerSource` is non-async (`IsAsync = false`), `Cost = Cheap`,
and the number of recipes is expected to be small (a handful per kind). The LINQ `OrderBy` in the
constructor runs once. `Query` uses simple iteration + `StringComparison.OrdinalIgnoreCase` for
filtering — minimal overhead.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] Production wiring in T7: register `RecipePickerSource` with `IPickerRegistry` and wire it to
  the new-from-recipe launcher
- [ ] Full `Hrot.Editor.AiShared.Tests` run (lead will confirm)
- [ ] No known issues — build 0 warnings, tests 0 failures

---

## 📁 Files Changed

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Browser/RecipePickerSource.cs` | **NEW** — `RecipeChoice` record + `RecipePickerSource` class |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/RecipePickerSourceTests.cs` | **NEW** — 4 tests with fakes |

No existing files modified. No production wiring. No drive-by edits.

---

## 🔨 Build Verification

```
dotnet build Hrot.Editor.AiShared.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## ✅ Definition of Done

- [x] `RecipeChoice` record added in `Hrot.Editor.AiShared.Browser`
- [x] `RecipePickerSource` class implements `IPickerSource<RecipeChoice>`, mirrors `AssetPickerSource`
- [x] `ToEntry` and `BuildEntries` are public
- [x] Per-kind recipes (incl. "Empty") projected with kind Category, per-kind IconKey, RecipeChoice Tag, optional description
- [x] The 4 exact named tests pass
- [x] Build = 0 warnings
- [x] Filtered `RecipePickerSource` tests = `Failed: 0`
- [x] No `BLUEPRINT_REGENERATE_SNAPSHOTS` used
- [x] No production wiring
- [x] No skipped/stubbed/weakened tests
- [x] Report written
