# BATCH-19 Report — Phase 4 Editor UI (JM-P4-001, JM-P4-002, JM-P4-003)

**Date:** 2026-05-29
**Branch:** `json-migration`
**Prereq commit:** `5b90ff57`

---

## Summary

All 10 implementation tasks completed. 8 new tests added (7 + 1), all passing.
Build succeeds with only pre-existing `Hrot.Blueprints.Tests` errors (CS0234/CS0246).

---

## Files Created

| File | Description |
|------|-------------|
| `Hrot/Subsystems/Hrot.Editor/Migration/MigrationAlertManager.cs` | New: per-session alert manager with `OnScenarioLoaded`, `OnScenarioCleared`, `SuppressAlertsForSession`, `HasPendingAlert`, `IsDegradedMode`, `Draw()` |
| `Hrot/Subsystems/Hrot.Editor.Tests/Migration/MigrationAlertManagerTests.cs` | New: 7 unit tests for `MigrationAlertManager` |

## Files Modified

| File | Changes |
|------|---------|
| `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs` | Added public `ListSidecarsAsync` wrapper delegating to `_storage` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs` | Added `_lastLoadResult`, `_lastLoadPath` fields; `LastLoadResult` property; switched `LoadScenario` from `ReadOnly` to `Persistent` adapter; updated `SaveScenario` to use `Persistent.SaveAsync` when prior load exists; added `GetSidecarsForLastLoadAsync` |
| `Hrot/Subsystems/Hrot.Editor/IEditorLogic.cs` | Added `bool IsScenarioDegraded { get; }` and `IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario()` + using for `Fdp.Core.Serialization.Migrations` |
| `Hrot/Subsystems/Hrot.Editor/EditorApplication.cs` | Added `_alertManager` field, `AlertManager` property; updated `NewScenario` to call `OnScenarioCleared`; updated `LoadScenario` to call `OnScenarioLoaded`; implemented `IsScenarioDegraded` and `GetMigrationSidecarsForCurrentScenario` |
| `Hrot/Subsystems/Hrot.Editor/Windows/EditorWindows.cs` | Updated `EditorBrowserWindow` to accept `MigrationAlertManager` param; calls `_alertManager.Draw()` from `DrawClientArea` |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Added `_editorApp` field alongside `_editorLogic`; passes `_editorApp!.AlertManager` to `EditorBrowserWindow` constructor; nulls `_editorApp` on teardown |
| `Hrot/Subsystems/Hrot.Editor/UI/ScenarioBrowserPanel.cs` | Added `_showMigrationHistoryDialog` and `_migrationSidecars` fields; added degraded-mode banner at top of `DrawContent`; added "Migration History" button; added `HandleMigrationHistoryClick`; added migration history modal |
| `Hrot/Subsystems/Hrot.Editor.Tests/ScenarioBrowserPanelTests.cs` | Added `HandleMigrationHistoryClick_CallsGetMigrationSidecarsForCurrentScenario` test; added `System.Collections.Generic` and `Fdp.Core.Serialization.Migrations` usings |

---

## Test Results

### New tests (BATCH-19)

```
Passed Hrot.Editor.Tests.Migration.MigrationAlertManagerTests.OnScenarioLoaded_WasMigrated_QueuesPendingAlert
Passed Hrot.Editor.Tests.Migration.MigrationAlertManagerTests.OnScenarioLoaded_WasNotMigrated_NoPendingAlert
Passed Hrot.Editor.Tests.Migration.MigrationAlertManagerTests.OnScenarioLoaded_IsDegraded_SetsDegradedMode
Passed Hrot.Editor.Tests.Migration.MigrationAlertManagerTests.OnScenarioLoaded_NotDegraded_NotDegradedMode
Passed Hrot.Editor.Tests.Migration.MigrationAlertManagerTests.OnScenarioLoaded_Null_NoEffect
Passed Hrot.Editor.Tests.Migration.MigrationAlertManagerTests.SuppressForSession_SubsequentMigratedLoad_NoPendingAlert
Passed Hrot.Editor.Tests.Migration.MigrationAlertManagerTests.OnScenarioCleared_ClearsCurrentResultAndPendingAlert
Passed Hrot.Editor.Tests.ScenarioBrowserPanelTests.HandleMigrationHistoryClick_CallsGetMigrationSidecarsForCurrentScenario

Total: 8 new tests, 8 passed, 0 failed
```

### Full Hrot.Editor.Tests run

```
Failed:  3 (pre-existing), Passed: 111 (existing + 8 new), Total: 114
```

Pre-existing failures (confirmed present in commit `5b90ff57` BEFORE batch-19 changes):
- `HrotEditor_HasNoCycloneDdsDependency` — CycloneDDS.Schema found in assembly references (pre-existing from a prior batch)
- `LoadScenario_UnrecognisedSubsystemType_Throws_AndLeavesRepoEmpty` — xUnit `Assert.Throws<T>` requires exact type; `MigrationException` does not satisfy `Assert.Throws<InvalidOperationException>` even though `MigrationException : InvalidOperationException` (pre-existing since BATCH-14 wired migration)
- `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount` — test expects `header.subsystemType` but serializer now writes `$meta` format (pre-existing since BATCH-14)

### Hrot.Common.Tests

```
Passed!  - Failed: 0, Passed: 46, Total: 46
```

---

## Deviations from Instructions

1. **`MakeResult` helper constructor**: The instructions provided `MakeResult` using object initializer syntax (`new DocumentMeta { DocType = ..., SchemaVersion = ... }`), but `DocumentMeta` has a positional constructor `DocumentMeta(string docType, int schemaVersion, ...)`. Fixed to use `new DocumentMeta("Hrot.Scenario", version)`.

2. **`ScenarioFileService.cs` usings**: Added `using System.Collections.Generic`, `using System.Threading`, `using System.Threading.Tasks` since `GetSidecarsForLastLoadAsync` uses `IReadOnlyList<>`, `CancellationToken`, and `Task<>`.

3. **`EditorSubsystem.cs` teardown**: Added `_editorApp = null` alongside `_editorLogic = null` in the teardown path to avoid stale references.

---

## Developer Insights

### Issues Encountered
- `DocumentMeta` uses a positional constructor, not an object initializer, so the test helper in the instructions needed a fix.
- The 3 pre-existing integration test failures looked concerning at first; git stash verification confirmed they predate BATCH-19.

### Weak Points Spotted
- The 3 pre-existing integration test failures should be tracked and fixed:
  - `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount` — the test was written before Phase 2 changed the serializer output format. The test should now assert `$meta.docType` instead of `header.subsystemType`.
  - `LoadScenario_UnrecognisedSubsystemType_Throws_AndLeavesRepoEmpty` — xUnit exact-type matching means the test needs `Assert.Throws<MigrationException>` (or it needs to verify the inner exception).
  - `HrotEditor_HasNoCycloneDdsDependency` — `CycloneDDS.Schema` appeared in assembly references at some prior point.

### Design Decisions Beyond Spec
- Used `_editorApp!` (null-forgiving) in `RegisterWindows` since `_editorApp` is always set at the same time as `_editorLogic`, and the null check `if (_editorLogic == null) return` already guards this path.
- Added `_editorApp = null` in teardown alongside `_editorLogic = null` to be consistent and avoid stale references.
