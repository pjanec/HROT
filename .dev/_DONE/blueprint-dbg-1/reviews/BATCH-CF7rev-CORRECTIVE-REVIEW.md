# BATCH-CF7rev-CORRECTIVE Review

**Batch:** BATCH-CF7rev-CORRECTIVE  
**Reviewer:** Development Lead  
**Date:** 2026-06-09  
**Status:** ✅ APPROVED

---

## Summary

Root cause confirmed and fixed. The production callback used plain `JsonSerializer.Deserialize<BlueprintAsset>` without `JsonStringEnumConverter` → string enums in `.bp.json` (`"Kind": "Function"`) caused `JsonException` → silently swallowed by `Console.WriteLine` (invisible in GUI editor) → no QuickReload occurred.

Diagnostic test proves it: broken path throws `JsonException`, fixed path (`BlueprintJsonServices.Deserialize`) succeeds.

---

## Verification Results

| Gate | Result |
|------|--------|
| Build | ✅ 0 errors, 0 warnings |
| CF7rev tests | ✅ 10/10 (8 original + 2 new diagnostic) |
| Full Blueprints suite | ✅ 7 pre-existing, 0 new (1683 pass, 8 skip, 1698 total) |

---

## Issues Found

No issues.

---

## Test Quality Assessment

**New tests:**
- `PlainJsonDeserialization_Fails...` — Proves the broken path throws on real `.bp.json` ✅
- `CallbackAssetLoading_Uses_BlueprintJsonServices...` — Validates the real production loading + compilation pipeline ✅

Both tests verify actual behavior (throwing, compilation success, DebugMap non-null/non-empty), not string presence.

---

## Verdict

**Status:** ✅ APPROVED

Root cause fixed. Ready for user re-test.

---

## 📝 Commit Message

```
fix: auto-instrumentation callback uses BlueprintJsonServices.Deserialize (CF-7-rev)

Root cause: EditorSubsystem callback used plain JsonSerializer.Deserialize without
JsonStringEnumConverter, causing silent deserialization failure on string enums
("Kind": "Function") in .bp.json files. No QuickReload occurred, so breakpoints
never paused without manual Compile.

Fix:
- EditorSubsystem callback now uses BlueprintJsonServices.Deserialize (single
  source of truth with JsonStringEnumConverter)
- Logging switched from Console.WriteLine to _hotReloadSource/IOutputConsole
  (now visible in editor's Message Log)
- Added success/failure log messages for observability

Test additions:
- PlainJsonDeserialization_Fails...: proves broken path throws JsonException
- CallbackAssetLoading_Uses_BlueprintJsonServices...: validates production path
- Clarified existing synthetic-callback test with comment

Gates: build 0/0, Blueprints 7/0-new (1683/1698), CF7rev tests 10/10
```
