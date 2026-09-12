# BATCH-35 Review — MTB2-T6

**Status:** ✅ APPROVED · **Date:** 2026-06-12 · Reviewer: Dev Lead

## Verified (independent)
- `RecipePickerSource` mirrors `AssetPickerSource`: `Query` iterates the kind→service registry (deterministic
  `_kinds` order) → `AvailableRecipes()` (incl "Empty"), text-filtered; `ToEntry` → Category `"<Kind>"` (or
  `"<Kind>/<sub>"` via injected `recipeCategory`), Name = recipe.Name, IconKey = `GetIconKey(kind)`, Tag =
  `RecipeChoice(kind, recipe)`, Description via injected `describe`; `GetItemKey = "{Kind}:{Name}"` (stable). Public
  `ToEntry`/`BuildEntries`. Injected delegates per D-T6-1.
- Tests assert real values: 2 "Empty" entries (one per kind) with correct `RecipeChoice.Kind` tags; Category/IconKey/
  Tag identity + `"Blueprint/AI"` sub-category; stable key; description present/absent. No tautologies/skips.
- 2 new files only; no production wiring (correct — that's T7).
- Build 0 warnings; **full `Hrot.Editor.AiShared.Tests` 1069/1069** (+4 new).

## Issues
None.

## Commit
`feat(main-toolbar2): RecipePickerSource — per-kind recipes (incl Empty) → Tree picker entries (MTB2-T6)`
