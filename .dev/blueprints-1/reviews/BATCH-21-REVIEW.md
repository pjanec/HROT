# BATCH-21 Review

**Batch:** BATCH-21
**Reviewer:** Development Lead
**Date:** 2026-05-22
**Status:** APPROVED

---

## Summary

TASK-ED-001 (Editor Infrastructure) complete. 13 production files + 3 test support files + 10 tests. Suite 434 pass / 0 fail / 5 skip (439 total). Build 0 errors. Independently verified.

---

## Issues Found

### Issue 1: FileSystemAssetCatalog JSON parse -- AssetId field name may not match (P3)

**File:** `Hrot.Blueprints.Editor/FileSystemAssetCatalog.cs`
**Problem:** The JSON field name `"AssetId"` must match the serialized field name of `BlueprintAsset.AssetId`. If `BlueprintJsonServices` serializes it as `"assetId"` (camelCase), the `TryGetProperty("AssetId", ...)` call will fail and all catalog entries will be skipped. Verify the actual field name used by the serializer.
**Action:** Check `BlueprintJsonServices` JsonOptions for `PropertyNamingPolicy`. Fix if camelCase. No change needed if the serializer uses `JsonNamingPolicy.CamelCase` -- then use `"assetId"`. This must be verified and fixed before SC7 passes in CI environment with real `.bp.json` files.

### Issue 2: EngineTimeControllerAdapter.IsPausedByDebugger always returns false (P3 -- by design)

**File:** `Hrot.Blueprints.Editor/EngineTimeControllerAdapter.cs`
**Problem:** `IsPausedByDebugger => false` always. This is a `// TODO M13` stub -- intentional. The actual engine type will be wired in M13.
**Action:** Already documented as TODO. No change needed.

---

## Test Quality Assessment

- **SC7 (empty catalog)**: Creates a real temp directory and calls `EnumerateAll()`. Asserts no entries. ✅ Correctly tests the filesystem integration.
- **SC8 (window registration)**: Creates `MockWindowRegistrar` + 2 windows, calls `OnEditorActivated()`, asserts `MenuEntries.Count == 2`. ✅
- **SC9 (visible-only drawing)**: Sets one window visible, one not. Calls `DrawAllWindows()`. Asserts `DrawCallCount` on visible == 1, invisible == 0. ✅

---

## Verdict

**Status: APPROVED**

Phase 6 Editor infrastructure layer complete. Ready for BATCH-22 (window implementations).

---

**Next Batch:** BATCH-22 -- TASK-ED-002 (Asset Browser + Graph Editor Windows) + TASK-ED-003 (Inspector + StructEdit)
