# BATCH-08 Report

**Batch:** BATCH-08  
**Developer:** GitHub Copilot  
**Tasks:** PACK2-F002 · PACK2-F003 · PACK2-F004  
**Status:** COMPLETED

---

## Files Modified / Created

| File | Action |
|------|--------|
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` | Modified — added `GlobalTime` singleton reset in `NewScenario()` and `LoadScenario()` |
| `Hrot.Editor.Tests/IntegrationTests/EditorFileOpsIntegrationTests.cs` | Created — 6 new integration tests |

---

## Build Result

| Project | Result |
|---------|--------|
| `Hrot.ScenarioEditor` | ✅ Build succeeded — 0 errors |
| `Hrot.Editor` | ✅ Build succeeded — 0 errors (unchanged) |
| `Hrot.Editor.Tests` | ✅ Build succeeded — 0 errors |

No new errors introduced. Pre-existing warnings (external deps, unrelated code) were not changed.

---

## Test Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| `Hrot.ScenarioEditor.Tests` | 14 | 14 | 0 |
| `Hrot.Editor.Tests` | 9 | 15 | +6 |

Both suites: **Passed — 0 failures, 0 skipped.**

> Note: `Hrot.Editor.Tests` had 9 tests before this batch (instructions estimated 8). Final count is 15, not 14. This is because the base count was 9, not 8 as specified — likely from a prior batch. All 6 new integration tests are present and passing.

---

## GlobalTime Reset Confirmation

- **`NewScenario`**: After `repo.SoftClear()`, the code now checks `repo.HasSingletonUnmanaged<GlobalTime>()` and calls `repo.SetSingletonUnmanaged(default(GlobalTime))` if true. Test `NewScenario_EmptiesRepo_AndResetsGlobalTime` seeds `GlobalTime.TotalTime = 42.0` then calls `NewScenario` → asserts `TotalTime == 0.0`. ✅ Passes.

- **`LoadScenario`**: Same pattern applied after `repo.SoftClear()` and before `_serializer.Deserialize(...)`. Test `LoadScenario_ResetsGlobalTime` seeds `GlobalTime.TotalTime = 99.0` then calls `LoadScenario` → asserts `TotalTime == 0.0`. ✅ Passes.

---

## Deviations from Instructions

1. **`Entities` JSON structure**: The instructions specified `entities.GetArrayLength()` but `ScenarioSerializer` writes `Entities` as a JSON **object** (dictionary keyed by entity ID), not a JSON array. Fixed to use `entities.EnumerateObject().Count()` instead. This is consistent with `ScenarioSerializerTests.cs` which accesses `(JsonObject)dom["Entities"]`.

2. **Test count delta**: Instructions predicted +6 tests taking `Hrot.Editor.Tests` from 8 → 14. Actual base count was 9, so final count is 15. All 6 new tests are present and green.

---

## Summary of Changes

### `ScenarioFileService.cs` — Task A

```csharp
// NewScenario — added after repo.SoftClear():
if (repo.HasSingletonUnmanaged<GlobalTime>())
    repo.SetSingletonUnmanaged(default(GlobalTime));

// LoadScenario — added after repo.SoftClear(), before _serializer.Deserialize():
if (repo.HasSingletonUnmanaged<GlobalTime>())
    repo.SetSingletonUnmanaged(default(GlobalTime));
```

No new `using` directives needed — `GlobalTime` is already in scope via existing `Fdp.Kernel` dependency.

### `EditorFileOpsIntegrationTests.cs` — 6 tests

| Test | Task | Description |
|------|------|-------------|
| `NewScenario_EmptiesRepo_AndResetsGlobalTime` | F002 | Via `ScenarioBrowserPanel.HandleNewClick` → GlobalTime reset to 0 |
| `NewScenario_WithoutGlobalTime_DoesNotThrow` | F002 | Guard: no GlobalTime registered → no throw |
| `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount` | F003 | JSON has `Header.SubsystemType == "Hrot.Scenario"` and 5 entities |
| `LoadScenario_RoundTrip_PreservesEntityCountAndComponents` | F004 | TestVector3 values survive save/load cycle |
| `LoadScenario_ResetsGlobalTime` | F004 | GlobalTime.TotalTime reset to 0 after load |
| `LoadScenario_UnrecognisedSubsystemType_Throws_AndLeavesRepoEmpty` | F004 | Unknown SubsystemType → `InvalidOperationException`, repo unmodified |
| `LoadScenario_HrotSimHostSubsystemType_Succeeds` | F004 | Cross-app: "Hrot.SimHost" file loads into Editor successfully |

> Note: 7 rows above but only 6 `[Fact]` methods — `F004-4` (`HrotSimHostSubsystemType_Succeeds`) is the 6th test, previously labelled F004-4 in the instructions summary but counts as test #6 within the class.
