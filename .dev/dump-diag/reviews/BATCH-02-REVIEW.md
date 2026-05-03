# BATCH-02 Review — dump-diag

**Date:** 2025-07-14
**Reviewer:** Dev Lead Agent
**Batch:** BATCH-02
**Status:** APPROVED

---

## Overall Assessment

BATCH-02 is approved. All five Phase 2 tasks are delivered and tested. The 28 new tests all pass.
Pre-existing failures in unrelated test classes are documented and not regressions.

---

## Task-by-Task Verification

### DD-P2-T01 — IDiagnosticEventHistoryService ✅

Verified: `IDiagnosticEventHistoryService.cs` and `DiagnosticEventHistoryService.cs` exist in
`Fdp.Core/Diagnostics/`. Circular buffer (capacity 500), thread-safe, correct `Capture()` and
`GetHistory()` signatures. `EventHistoryCaptureSystem` correctly placed in `PostSimulation` phase.
5 tests, all logic-checking assertions. **PASS**

### DD-P2-T02 — EventBrowserPanel Refactor ✅

Verified: `EventBrowserPanel.cs` constructor takes `IDiagnosticEventHistoryService`. No
`RegisterBus` or `Update()` methods. `DrawContent()` calls `GetHistory()` directly. All 5
subsystems (`SimHostVisualization`, `SimHostApp`, `CgfSubsystem`, `IgApplication`,
`EditorSubsystem`) wired correctly. 3 mock-based tests covering render path, pause behaviour, and
null guard. **PASS**

### DD-P2-T03 — IArchitectureDiagnosticsService ✅

Verified: Interface + DTOs in `Fdp.ModuleHost/Diagnostics/`. `ArchitectureDiagnosticsService`
has both the `Func<ModuleHostKernel?>` lazy constructor and the convenience `ModuleHostKernel`
constructor. Returns empty snapshot when kernel is null — correct defensive behaviour.
`ArchitectureDiagnosticsPanel` and `ArchitectureDiagnosticsWindow` updated; all 4 subsystem
instantiation sites pass `new ArchitectureDiagnosticsService(() => ...)`. **PASS**

Note: No tests for T03 — recorded in DEBT-TRACKER as P3.

### DD-P2-T04 — IEntityStateExtractionService ✅

Verified: `IEntityStateExtractionService.cs` and `EntityStateExtractionService.cs` in
`Fdp.Toolkits/Diagnostics/`. Entity iteration uses the correct `MaxEntityIndex` idiom (not a
non-existent `GetAllEntities()`). Filter, dead-entity skip, component extraction, and NetworkId
resolution all present. 8 tests with value-checking assertions. **PASS**

**Test quality note:** Test components required `[ComponentId]` attributes — the developer handled
this correctly with IDs 220–221, which do not conflict with the 210–219 range used by Scenario
tests.

### DD-P2-T05 — ILogArchiveExtractionService ✅

Verified: `ILogArchiveExtractionService.cs` in `Hrot.Core/Diagnostics/`. `LogArchiveExtractionService`
uses `ReadOnlySpan<char>` for line parsing — no `string.Split`. Supports NLog pipe format and
bracket format. Opens files with `FileShare.ReadWrite`. Fully async with O(1) memory per file.
`HrotNodeConfig.LogDirectory` added with default `string.Empty`. 12 tests covering: null guards,
missing directory, no matching files, all-lines copy, severity filtering (both formats), multiple
files, file age filter (old skipped, recent included), and `HrotNodeConfig` property. **PASS**

---

## Scope Verification

- DD-P5-T03 (`HrotNodeConfig.LogDirectory`) is also covered — marked complete in TASK-TRACKER.
- No unintended scope creep detected.

---

## Code Quality Observations

1. **`try/catch` in `GetRawObject` path (EntityStateExtractionService)** — acceptable workaround
   for current API; recorded in DEBT-TRACKER as P3.
2. **File-level age filter in `LogArchiveExtractionService`** — limitation acknowledged; recorded
   in DEBT-TRACKER as P3 and deferred to Batch-05 when NLog layout will be defined.
3. **`ArchitectureDiagnosticsService` untested** — P3 debt recorded.

---

## Suggested Git Commit Message

```
feat(dump-diag): implement Phase 2 diagnostic service interfaces (BATCH-02)

DD-P2-T01: IDiagnosticEventHistoryService + DiagnosticEventHistoryService
  - Thread-safe circular buffer (capacity=500) in Fdp.Core.Diagnostics
  - EventHistoryCaptureSystem (PostSimulation phase) in Fdp.ModuleHost.Diagnostics

DD-P2-T02: Refactor EventBrowserPanel to IDiagnosticEventHistoryService
  - Constructor injection; removed RegisterBus/Update methods
  - Wired in SimHostVisualization, SimHostApp, CgfSubsystem, IgApplication, EditorSubsystem

DD-P2-T03: IArchitectureDiagnosticsService + ArchitectureDiagnosticsService
  - Lazy Func<ModuleHostKernel?> constructor for DI before kernel availability
  - ArchitectureDiagnosticsPanel/Window updated; all 4 subsystems wired

DD-P2-T04: IEntityStateExtractionService + EntityStateExtractionService
  - MaxEntityIndex-based iteration (correct codebase idiom)
  - NetworkId resolution, component extraction, networkId filter

DD-P2-T05: ILogArchiveExtractionService + LogArchiveExtractionService
  - ReadOnlySpan<char> line parsing; no string.Split
  - Supports NLog pipe and bracket severity formats
  - FileShare.ReadWrite for live-process safety; O(1) memory per file
  - HrotNodeConfig.LogDirectory property added (DD-P5-T03)

Tests: 28 new tests, 28 passing
  Fdp.Core.Tests:         5 (DiagnosticEventHistoryService)
  Fdp.Presentation.Tests: 3 (EventBrowserPanel)
  Fdp.Toolkits.Tests:     8 (EntityStateExtractionService)
  Hrot.Core.Tests:       12 (LogArchiveExtractionService, HrotNodeConfig)
```
