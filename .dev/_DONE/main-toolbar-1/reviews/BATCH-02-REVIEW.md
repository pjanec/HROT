# BATCH-02 Review
**Status:** ✅ APPROVED (after review-round-2 corrective)   **Date:** 2026-06-10

## Summary
Phase-0 folder migration: `.bp/.hsm/.btree.json` moved to the §16 `Assets/*` + `Recipes/Blueprints`
layout (clean `git mv` renames), csproj globs repointed, consumers + tests repointed to `AssetRoots`
(now with relative-segment helpers). MTB-P0-T2 + MTB-P0-T3 complete.

## Issues Found
### Issue 1 (resolved): named T2 test `FolderLayoutTests.Output_HasAssetsAndRecipesRoots` missing
**Problem:** the named success-condition test was not created in round 1. Root cause: its wording
assumed finals ship to **output** under `Assets/<Kind>`, but finals are generator `AdditionalFiles`
compiled into the assembly — only recipes (`Content`) ship to output. **Fix:** bounced back
(DEC-8); worker added `FolderLayoutTests` asserting the achievable invariants (recipes in output
`Recipes/Blueprints`; source `Assets/{Blueprints,HSMs,BTrees}` + `Recipes/Blueprints` populated;
no leftover bare `Blueprints/`/`Machines/`/`Trees/`). Verified passing.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → **0 errors, 0 new warnings** (TWAE clean).
- New tests run by lead: FolderLayout(1) + AssetScan(2) + DiscoverRecipes(2) + AssetRoots relative(7) → **all pass unfiltered**.
- **Pre-existing-failure audit (the key check):** worker reported 9 Blueprints.Tests + 2
  Generators.Tests failures as "pre-existing". Verified by running those exact tests in a detached
  worktree at the pre-move baseline `e3bf645a` — **all 11 fail identically there**. Confirmed NOT
  introduced by this batch. (CF2/CF7rev fail on the breakpoint-pause logic even with the old path =
  the known breakpoint-ID-drift issue, proving the path repoint worked.)
- Changed-file set audited: exactly the planned scope (moves + csproj + listed consumers + listed
  test path updates + AssetRoots helpers + 3 new test files). **No scope creep, no legacy deletions.**

## Test Quality
Strong. `AssetScanTests.RecipesExcludedFromFinalScan` builds a real `BlueprintAssetContributor`,
writes real files in both roots, and asserts final-returned / recipe-excluded on actual enumerated
values. `DiscoverRecipesTests` invokes the real `DiscoverRecipes()` and asserts CountingDemo loads
from the new output root (transitively proving the Content copy glob). No tautological/skipped tests.

## Verdict
APPROVED. MTB-P0-T2 + MTB-P0-T3 → `[x]`. Phase 0 complete.

## Commit Message
```
feat(main-toolbar): reorganize asset/recipe folders to §16 layout (MTB-P0-T2, MTB-P0-T3)

Move .bp/.hsm/.btree.json into Assets/{Blueprints,HSMs,BTrees} + Recipes/Blueprints
(git mv renames); repoint Hrot.AI.Behaviors.csproj globs. Add AssetRoots relative-
segment helpers (AssetsRelative/RecipesRelative/ScenariosRecipesRelative) and re-express
the absolute props on them. Repoint BlueprintEditorBootstrap.DiscoverRecipes (output dir)
and EditorSubsystem project-dir consumers; update 9 tests' hardcoded fixture paths.
Tests: FolderLayoutTests, AssetScanTests(2), DiscoverRecipesTests(2), AssetRoots relative(7).
Pre-existing failures (9 Blueprints + 2 Generators) verified present at pre-move baseline.
```
