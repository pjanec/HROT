# BATCH-37 REPORT — BUG-A1: New Asset crash

**Date:** 2026-06-12  
**Branch:** blueprint-integ-1  
**Commit:** (working tree)  
**Model:** flash (prescribed)

---

## Summary

Fixed BUG-A1: the `ShowNewAssetDialog` local function in `EditorSubsystem.RegisterWindows` was passing the **minted adapter** (`IEditableAsset`, e.g. `BlueprintEditableAssetAdapter`) directly to `_aiDocumentManager.Open`, which requires a **catalogued concrete asset** (e.g. `BlueprintFileAsset`). This caused `System.ArgumentException` at runtime. Scenario is skipped (not document-backed).

---

## Change made

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

**What:** Replaced the `onCreated` callback in `ShowNewAssetDialog` (lines 2493–2499) to:

1. Check if the minted asset's `Kind` is one of `Blueprint`, `BTree`, or `Hsm` (document-backed kinds).
2. For those kinds, resolve the catalogued asset via `_aiCatalogBuilder.Catalog.FindByAssetId(minted.AssetId)`.
3. Open the catalogued asset if found; otherwise set a status message noting the catalog may need a refresh.
4. Scenario (`Scenario` kind) is explicitly skipped — it has no callback action.

This mirrors the retired `RecipeCreateModal` behaviour.

---

## Build result

```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
Build succeeded. 0 Warning(s) 0 Error(s)
```

---

## Test result

```
dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
(BLUEPRINT_REGENERATE_SNAPSHOTS not set)

Passed!  - Failed:     0, Passed:   186, Skipped:     0, Total:   186, Duration: 822 ms
```

All 186 existing tests pass. No tests were weakened, skipped, or modified.

---

## Deviations

None. The edit matches the prescribed replacement exactly.

---

## Known issues

None.
