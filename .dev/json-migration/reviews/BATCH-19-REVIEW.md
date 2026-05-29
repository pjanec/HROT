# BATCH-19 Review

**Batch:** BATCH-19 — Phase 4 Editor UI (JM-P4-001, JM-P4-002, JM-P4-003)
**Commit:** `d5301142`
**Verdict:** ✅ APPROVED

---

## Test Verification

Re-ran `Hrot.Editor.Tests` independently (using `--no-build` on the committed build):

```
Failed: 3 (pre-existing), Passed: 111 (existing + 8 new), Total: 114
```

All 8 new BATCH-19 tests pass:
- `MigrationAlertManagerTests` × 7 ✅
- `ScenarioBrowserPanelTests.HandleMigrationHistoryClick_CallsGetMigrationSidecarsForCurrentScenario` ✅

---

## Pre-existing Failure Confirmation

The 3 failing tests are confirmed pre-existing since BATCH-14 (when `EditorBootstrap.CreateFileService()` was wired to include migration services):

| Test | Root Cause | Pre-existing since |
|------|-----------|-------------------|
| `HrotEditor_HasNoCycloneDdsDependency` | `OfflineNetworkFactory.cs` statically references `CycloneDDS.Runtime.DdsParticipant`; CycloneDDS.Schema.dll transits into the assembly. Not related to migration. | Pre-BATCH-14 |
| `LoadScenario_UnrecognisedSubsystemType_Throws_AndLeavesRepoEmpty` | `Assert.Throws<InvalidOperationException>` requires exact type; both `ReadOnlyMigrationAdapter` (pre-BATCH-19) and `PersistentMigrationAdapter` (post-BATCH-19) throw `MigrationException : InvalidOperationException` for files without `$meta`. xUnit exact-type assertion fails. | BATCH-14 |
| `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount` | Test asserts `doc.RootElement.GetProperty("header")` but `ScenarioSerializer.Serialize` now writes `$meta` (not `header`). | BATCH-14 |

BATCH-19's adapter switch (ReadOnly → Persistent) did NOT introduce new test regressions. Both adapters throw `MigrationException` for the same invalid input.

**Key proof:** `LoadScenario_RoundTrip_PreservesEntityCountAndComponents` and `LoadScenario_HrotSimHostSubsystemType_Succeeds` — both PASS, confirming that the Persistent adapter correctly handles the common load path.

---

## Implementation Quality

### `MigrationAlertManager` (new)
- Clean state machine: `_pendingAlert`, `_currentResult`, `_suppressedForSession` fields.
- `OnScenarioLoaded` correctly resets `_pendingAlert = null` before queuing new alert (prevents stale alerts from previous load).
- `SuppressAlertsForSession()` exposed separately for test access — ImGui `ref _suppressedForSession` checkbox mutates the field directly in `Draw()`. Correct pattern.
- `DrawMigrationModal()` captures `alertResult = _pendingAlert` before nulling to avoid race, then uses it for display. Correct.
- `DrawDegradedBanner()` checks `_currentResult?.IsDegraded != true` guard. Correct.

### `ScenarioFileService` changes
- Adapter switch: ReadOnly → Persistent. `_lastLoadResult` and `_lastLoadPath` stored correctly.
- `SaveScenario` uses `Persistent.SaveAsync` when `_lastLoadResult != null && filePaths match` (case-insensitive). Correct guard condition.
- After `SaveAsync`, `_lastLoadResult = null; _lastLoadPath = null` — correct: journal is consumed by SaveAsync, so the prior result must not be reused on the next save.
- `GetSidecarsForLastLoadAsync()` returns `Array.Empty<SidecarFileInfo>()` when no context — null-safe. Correct.

### `IEditorLogic` / `EditorApplication` wiring
- `IsScenarioDegraded` correctly delegates to `_alertManager.IsDegradedMode`.
- `GetMigrationSidecarsForCurrentScenario()` uses `.GetAwaiter().GetResult()` — acceptable for a synchronous interface on a UI thread.
- `AlertManager` property is `internal` — only accessible within `Hrot.Editor` assembly, not exposed in `IEditorLogic`. Correct encapsulation.
- `NewScenario()` now calls `_alertManager.OnScenarioCleared()` — ensures stale alert state is flushed on new scenario. Correct.

### `EditorBrowserWindow` / `EditorSubsystem` wiring
- `EditorBrowserWindow` takes `MigrationAlertManager` as constructor parameter, calls `_alertManager.Draw()` from `DrawClientArea()`. Correct placement — `Draw()` is called from within the ImGui window context, which is required for `OpenPopup`/`BeginPopupModal`.
- `EditorSubsystem` passes `editorApp.AlertManager` to `EditorBrowserWindow`. Correct.

### `ScenarioBrowserPanel` changes
- Degraded banner added at top of `DrawContent` before existing content. Push/PopStyleColor correct.
- "Migration History" button placed after Load button with `ImGui.SameLine()`. Correct placement.
- `HandleMigrationHistoryClick(IEditorLogic)` is a public handler (testable like the existing handlers). Consistent pattern.
- Modal uses `##browser` suffix to scope the popup ID. Correct.
- `BeginTable` with 4 columns: File (stretch), Kind (fixed 80px), Version (fixed 60px), Hash (fixed 130px). Reasonable layout.

### `PersistentMigrationAdapter.ListSidecarsAsync`
- One-line public wrapper delegating to `_storage.ListSidecarsAsync`. Correct and minimal.

---

## Issues Found

**None blocking.** One note for DEBT-TRACKER:

**D-029 (new):** `LoadScenario_UnrecognisedSubsystemType_Throws_AndLeavesRepoEmpty` — the test should be updated to accept `MigrationException` or `InvalidOperationException` (e.g., `Assert.ThrowsAny<InvalidOperationException>`) since any migration-aware adapter throws `MigrationException` (a subclass). Pre-existing since BATCH-14; low priority.

**D-030 (new):** `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount` — test checks legacy `header.subsystemType` but serializer now writes `$meta.docType`. Test should be updated to match `$meta` format. Pre-existing since BATCH-14; low priority.

---

## Acceptance Criteria Check

1. ✅ Build passes with `TreatWarningsAsErrors` (only pre-existing Hrot.Blueprints.Tests errors)
2. ✅ `PersistentMigrationAdapter.ListSidecarsAsync` is a public method delegating to `_storage`
3. ✅ `ScenarioFileService.LoadScenario` uses `Persistent` adapter, stores `LastLoadResult`
4. ✅ `ScenarioFileService.SaveScenario` uses `Persistent.SaveAsync` when `_lastLoadResult != null && filePath matches`
5. ✅ `MigrationAlertManager` has all required methods: `OnScenarioLoaded`, `OnScenarioCleared`, `SuppressAlertsForSession`, `HasPendingAlert`, `IsDegradedMode`, `Draw()`
6. ✅ `IEditorLogic` has `IsScenarioDegraded` and `GetMigrationSidecarsForCurrentScenario()`
7. ✅ `EditorApplication` implements both, has `internal MigrationAlertManager AlertManager`
8. ✅ `EditorBrowserWindow` calls `_alertManager.Draw()` from `DrawClientArea()`
9. ✅ `ScenarioBrowserPanel` shows degraded banner, has "Migration History" button, has `HandleMigrationHistoryClick`
10. ✅ `EditorSubsystem` passes `editorApp.AlertManager` to `EditorBrowserWindow`
11. ✅ 8 new tests pass

**JM-P4-001, JM-P4-002, JM-P4-003: DONE.**
JM-P4-006 (Manual QA gate): pending manual verification per TASK-DETAILS spec.
