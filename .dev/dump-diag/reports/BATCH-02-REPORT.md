# BATCH-02 Report — dump-diag

**Date:** 2025-07-14
**Workstream:** dump-diag
**Batch:** BATCH-02

---

## Summary

All five tasks in BATCH-02 have been implemented. All new tests pass. No regressions introduced in the affected projects.

---

## Task Status

| Task ID   | Title                                      | Status    | Tests |
|-----------|--------------------------------------------|-----------|-------|
| DD-P2-T01 | IDiagnosticEventHistoryService + impl      | Completed | 5/5   |
| DD-P2-T02 | Refactor EventBrowserPanel to use service  | Completed | 3/3   |
| DD-P2-T03 | IArchitectureDiagnosticsService + impl     | Completed | 0 new tests (service wraps kernel directly) |
| DD-P2-T04 | IEntityStateExtractionService + impl       | Completed | 8/8   |
| DD-P2-T05 | ILogArchiveExtractionService + HrotNodeConfig.LogDirectory | Completed | 12/12 |

---

## Detailed Task Notes

### DD-P2-T01 — IDiagnosticEventHistoryService (from prior session)

- Interface + DTOs: `FDP/Engine/Fdp.Core/Diagnostics/IDiagnosticEventHistoryService.cs`
- Implementation: `FDP/Engine/Fdp.Core/Diagnostics/DiagnosticEventHistoryService.cs`
  - Thread-safe circular buffer, capacity=500
- ECS capture system: `FDP/Engine/Fdp.ModuleHost/Diagnostics/EventHistoryCaptureSystem.cs`
- Tests: `Fdp.Core.Tests/Diagnostics/DiagnosticEventHistoryServiceTests.cs` — 5 tests

### DD-P2-T02 — EventBrowserPanel refactor (from prior session)

- `Fdp.Presentation/ImGui/Panels/EventBrowserPanel.cs` — replaced; now takes `IDiagnosticEventHistoryService` constructor, `DrawContent()` calls `GetHistory()` each frame
- Updated 5 subsystems: SimHostVisualization, SimHostApp, CgfSubsystem, IgApplication, EditorSubsystem
  - Each creates `DiagnosticEventHistoryService`, passes it to panel, registers `EventHistoryCaptureSystem` in the kernel
  - Removed `Update()` calls (no longer needed)
- Tests: `Fdp.Presentation.Tests/ImGui/EventBrowserPanelTests.cs` — 3 mock-based tests

### DD-P2-T03 — IArchitectureDiagnosticsService (from prior session)

- Interface + DTOs: `FDP/Engine/Fdp.ModuleHost/Diagnostics/IArchitectureDiagnosticsService.cs`
  - Key types: `ModuleDiagnosticsDto`, `SystemDiagnosticsRow`, `TranslatorDiagnosticsDto`, `ArchitectureSnapshotDto`
  - Method: `ArchitectureSnapshotDto GetSnapshot()`
- Implementation: `FDP/Engine/Fdp.ModuleHost/Diagnostics/ArchitectureDiagnosticsService.cs`
  - Two constructors: `Func<ModuleHostKernel?>` (lazy, for DI before kernel is available) and `ModuleHostKernel` (convenience)
  - Returns empty snapshot when kernel is null
- `ArchitectureDiagnosticsPanel.cs` — replaced; constructor takes `IArchitectureDiagnosticsService`; `DrawContent()` takes no args
- `ArchitectureDiagnosticsWindow.cs` — updated; removed `Func<ModuleHostKernel?>` ctor param
- Updated 4 subsystem files to pass `new ArchitectureDiagnosticsService(() => _app?.Kernel)`

### DD-P2-T04 — IEntityStateExtractionService

- Interface + DTOs: `FDP/Toolkits/Fdp.Toolkits/Diagnostics/IEntityStateExtractionService.cs`
  - DTO: `EntityStateDumpDto` with `NetworkId`, `LocalIndex`, `LocalGeneration`, `Components`
  - Method: `IReadOnlyList<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? networkIds = null)`
- Implementation: `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EntityStateExtractionService.cs`
  - Constructor: `(EntityRepository repo, NetworkEntityMap? entityMap = null)`
  - Entity iteration: `for (int i = 0; i <= _repo.MaxEntityIndex; i++) { var e = _repo.GetEntityByIndex(i); ... }`
    - There is no `GetAllEntities()` on `EntityRepository`; the codebase-wide idiom is index-based iteration using `MaxEntityIndex`
  - Resolves `NetworkId` from `NetworkIdentity` component when present
  - Extracts component data via `GetRegisteredComponentTypes()` + `GetRawObject(entity.Index)`
- Tests: `Fdp.Toolkits.Tests/Diagnostics/EntityStateExtractionServiceTests.cs` — 8 tests
  - Tests cover: null repo, empty repo, all-alive, dead-entity-excluded, networkId filter, component dict populated, local index/generation, empty filter

**Issue encountered:** `EntityRepository.GetAllEntities()` does not exist. Used index-based iteration (correct codebase idiom) instead.

**Test-only components:** Required `[ComponentId(220)]` and `[ComponentId(221)]` attributes with `[StructLayout(LayoutKind.Sequential)]` on test-only structs — this is an `EntityRepository` registration constraint.

### DD-P2-T05 — ILogArchiveExtractionService + HrotNodeConfig.LogDirectory

- `HrotNodeConfig.LogDirectory` property added: `public string LogDirectory { get; set; } = string.Empty;`
- Interface: `Hrot/Engine/Hrot.Core/Diagnostics/ILogArchiveExtractionService.cs`
  - Method: `Task<int> ExtractLogsAsync(string targetFilePath, int severityThreshold, float maxAgeHours, CancellationToken ct = default)`
- Implementation: `Hrot/Engine/Hrot.Core/Diagnostics/LogArchiveExtractionService.cs`
  - Constructor: `(string logDirectory, string subsystemName, int nodeId)`
  - File glob: `{subsystemName}_{nodeId}*.log`
  - File-level age filter using `File.GetLastWriteTimeUtc` vs `maxAgeHours`
  - Opens each file with `FileShare.ReadWrite` (safe for live processes)
  - Line filtering via `ReadOnlySpan<char>` slicing — no `string.Split`
    - Supports NLog pipe format: `HH:mm:ss.fff | LEVEL | ...`
    - Supports bracket format: `[LEVEL]` or `[N]`
    - Unknown lines pass through (fail-safe)
  - Fully async; O(1) memory per file
- Tests: `Hrot.Core.Tests/Diagnostics/LogArchiveExtractionServiceTests.cs` — 12 tests

---

## Issues Encountered

1. **`EntityRepository.GetAllEntities()` does not exist** — No such method. Replaced with `MaxEntityIndex`-based index loop. This is the established idiom across `RepositorySerializer`, `ScenarioSerializer`, and test helpers.

2. **`[ComponentId]` required on all components** — Test-only structs needed explicit `[ComponentId]` attributes to be registered with `EntityRepository`. Used IDs 220–221 (above the 210–219 range used by `Fdp.Toolkit.Scenario.Tests`).

3. **`StreamWriter` `leaveOpen` param not available in all overloads** — Used the simpler `new StreamWriter(path, append: false)` overload.

---

## Weak Points Spotted

1. **`GetRawObject(entity.Index)` in `EntityStateExtractionService`** — `IComponentTable.GetRawObject` may return null for unmanaged components stored outside managed heap. The `try/catch` around it is a reasonable workaround but the API is fragile. If unmanaged components need to be exposed, a proper boxing path should be added to `IComponentTable`.

2. **`LogArchiveExtractionService` has no per-line timestamp parsing** — Only file-level age filtering is applied. If logs need per-line time filtering, the NLog layout would need to include a date component (currently only `${time}` = time of day, no date). This is acceptable for the current use case (files are named by run session) but is a known limitation.

3. **`ArchitectureDiagnosticsService` tests not written** — T03 did not produce unit tests because the service wraps `ModuleHostKernel` directly and the kernel is not easily mockable. This is recorded as P3 tech debt.

---

## Design Decisions Made Beyond the Spec

- **`LogArchiveExtractionService` returns `int` (lines written)** rather than `void` — makes testing straightforward and enables the caller to log or display how many lines were archived.
- **`ExtractLogsAsync` returns 0 (not throw)** when `LogDirectory` is missing or empty — consistent with the "graceful no-op" pattern used throughout the codebase.

---

## Test Results (New Tests Only)

| Suite | New Tests | Passed |
|-------|-----------|--------|
| `Fdp.Core.Tests` (DiagnosticEventHistoryService) | 5 | 5 |
| `Fdp.Presentation.Tests` (EventBrowserPanel) | 3 | 3 |
| `Fdp.Toolkits.Tests` (EntityStateExtractionService) | 8 | 8 |
| `Hrot.Core.Tests` (LogArchiveExtractionService) | 12 | 12 |
| **Total** | **28** | **28** |

Pre-existing failures in `Fdp.Core.Tests` (1), `Fdp.Presentation.Tests` (3), and `Fdp.Toolkits.Tests` (23) are unrelated to BATCH-02 work.
