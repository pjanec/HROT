# BATCH-02: Diagnostic Data Service Interfaces and Implementations

**Batch Number:** BATCH-02
**Tasks:** DD-P2-T01, DD-P2-T02, DD-P2-T03, DD-P2-T04, DD-P2-T05
**Phase:** Phase 2 — Diagnostic Data Service Interfaces and Implementations
**Estimated Effort:** 14-18 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (FdpJsonOptionsRegistry must be available)

---

## Onboarding & Workflow

### Developer Instructions

This batch creates the five headless diagnostic services that extract data from the simulation.
These services are consumed both by UI panels and by the cluster dump handler (Phase 5). The
services must be completely independent of ImGui or Raylib.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Task Detail:** `.dev/dump-diag/TASK-DETAIL.md` — DD-P2-T01 through DD-P2-T05
3. **Design Document:** `.dev/dump-diag/DESIGN.md` — Sections 2.1, 2.2, 2.3, 2.4
4. **Previous Review:** `.dev/dump-diag/reviews/BATCH-01-REVIEW.md`

### Source Code Locations — Existing Files to Study

- **EventBrowserPanel:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/EventBrowserPanel.cs`
  - Has the existing event capture loop (CapturedEvent class, FdpEventBus.GetDebugInspectors())
  - DD-P2-T02 refactors this panel to use the new service
- **ArchitectureDiagnosticsPanel:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/ArchitectureDiagnosticsPanel.cs`
  - DD-P2-T03 extracts its data-gathering into a service
- **EntityInspectorPanel:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityInspectorPanel.cs`
  - DD-P2-T04 adds extraction service alongside it
- **EntityJsonDumper:** `FDP/Engine/Fdp.Presentation/ImGui/Utils/EntityJsonDumper.cs`
  - Contains the existing entity dump logic used by DD-P2-T04
- **Hrot.Core Infrastructure:** `Hrot/Engine/Hrot.Core/Infrastructure/` — find `HrotNodeConfig.cs`
  - DD-P2-T05 relates to this config
- **FdpEventBus:** `FDP/Engine/Fdp.Core/FdpEventBus.cs` — understand `GetDebugInspectors()`

### New Files to Create

- `FDP/Engine/Fdp.Core/Diagnostics/IDiagnosticEventHistoryService.cs`
- `FDP/Engine/Fdp.Core/Diagnostics/DiagnosticEventHistoryService.cs`
- `FDP/Engine/Fdp.ModuleHost/Diagnostics/IArchitectureDiagnosticsService.cs`
- `FDP/Engine/Fdp.ModuleHost/Diagnostics/ArchitectureDiagnosticsService.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/IEntityStateExtractionService.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EntityStateExtractionService.cs`
- `Hrot/Engine/Hrot.Core/Diagnostics/ILogArchiveExtractionService.cs`
- `Hrot/Engine/Hrot.Core/Diagnostics/LogArchiveExtractionService.cs`

### Test Projects

- `FDP/Engine/Fdp.Core.Tests/` — tests for DD-P2-T01
- `FDP/Engine/Fdp.Presentation.Tests/` (if it exists) — tests for DD-P2-T02
- Tests for DD-P2-T03: locate or create in Fdp.ModuleHost.Tests
- Tests for DD-P2-T04: in Fdp.Toolkits.Tests or a new diagnostics test file
- Tests for DD-P2-T05: in Hrot.Core.Tests (locate it) or similar

### Build Commands

```
cd FDP
dotnet build FDP.sln
```

For Hrot:

```
cd Hrot
dotnet build  (or locate the Hrot solution file)
```

Or build individual projects:

```
dotnet build FDP/Engine/Fdp.Core/Fdp.Core.csproj
dotnet build FDP/Engine/Fdp.ModuleHost/Fdp.ModuleHost.csproj
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj
dotnet build Hrot/Engine/Hrot.Core/Hrot.Core.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/dump-diag/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/dump-diag/questions/BATCH-02-QUESTIONS.md`

---

## Context

These services form the data layer for Phase 5 (node-side dump handler). Each service provides
a clean `GetXxx()` method returning a DTO array/object. The dump handler will call all four
services during a diagnostic dump operation, then serialise the results using
`FdpJsonOptionsRegistry.Indented` + `JsonAestheticFormatter.FlattenNumericArrays` from Phase 1.

The services are headless — no ImGui, no Raylib, no render thread dependencies.

**Related Tasks:**
- [DD-P2-T01](../TASK-DETAIL.md#dd-p2-t01--idiagnosticeventhistoryservice-and-capturedeventdto) — Event history service
- [DD-P2-T02](../TASK-DETAIL.md#dd-p2-t02--refactor-eventbrowserpanel-to-use-idiagnosticeventhistoryservice) — Refactor EventBrowserPanel
- [DD-P2-T03](../TASK-DETAIL.md#dd-p2-t03--iarchitecturediagnosticsservice) — Architecture diagnostics service
- [DD-P2-T04](../TASK-DETAIL.md#dd-p2-t04--ientitystateextractionservice) — Entity state extraction service
- [DD-P2-T05](../TASK-DETAIL.md#dd-p2-t05--ilogarchiveextractionservice) — Log archive extraction service

---

## Batch Objectives

1. Create `IDiagnosticEventHistoryService` + `DiagnosticEventHistoryService` (thread-safe
   circular buffer with copy-under-lock snapshot semantics)
2. Refactor `EventBrowserPanel` to consume the new service instead of its private capture loop
3. Create `IArchitectureDiagnosticsService` + `ArchitectureDiagnosticsService` (extracts from
   `ModuleHostKernel`)
4. Create `IEntityStateExtractionService` + `EntityStateExtractionService` (wraps
   `EntityJsonDumper.Dump` and `NetworkIdentity`)
5. Create `ILogArchiveExtractionService` + `LogArchiveExtractionService` (streaming log
   read/filter/write using `ReadOnlySpan<char>`)

---

## Tasks

### Task 1: IDiagnosticEventHistoryService (DD-P2-T01)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p2-t01--idiagnosticeventhistoryservice-and-capturedeventdto)

Create `FDP/Engine/Fdp.Core/Diagnostics/IDiagnosticEventHistoryService.cs`:

```csharp
namespace Fdp.Core.Diagnostics;

public record CapturedEventDto(uint Frame, string TypeName, bool IsManaged, string Summary, object? RawEvent);

public interface IDiagnosticEventHistoryService
{
    void Capture(FdpEventBus eventBus);
    CapturedEventDto[] GetHistory(IReadOnlyList<string>? providerFilter = null);
}
```

Create `FDP/Engine/Fdp.Core/Diagnostics/DiagnosticEventHistoryService.cs`:
- Circular buffer capped at 500 events
- `Capture(bus)`: reads `bus.GetDebugInspectors()`, calls `inspector.InspectReadBuffer()`,
  fills buffer (same logic as current EventBrowserPanel's private capture code)
- `GetHistory(filter)`: acquires lock, copies all entries to a new array, releases lock,
  returns array. If `filter` is null/empty, return all. Otherwise filter by `TypeName` prefix.

**Namespace:** `Fdp.Core.Diagnostics`

**Tests required (in Fdp.Core.Tests/Diagnostics/):**
- Push 600 events, `GetHistory()` returns exactly 500
- `GetHistory(new[] { "World" })` returns only "World" prefix events
- Concurrent read + write does not throw
- Snapshot returned by `GetHistory()` is stable — writes after the call do not modify it

---

### Task 2: Refactor EventBrowserPanel (DD-P2-T02)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p2-t02--refactor-eventbrowserpanel-to-use-idiagnosticeventhistoryservice)

Inject `IDiagnosticEventHistoryService` into `EventBrowserPanel`. Remove the panel's private
`_history`, `_capacity = 500`, and the capture loop that calls
`FdpEventBus.GetDebugInspectors()`. The panel reads from the service instead.

Replace `CapturedEvent` private class with `CapturedEventDto` from `Fdp.Core.Diagnostics`.

Register `DiagnosticEventHistoryService` as a singleton in subsystem bootstrappers. Create a
companion `EventHistoryCaptureSystem` implementing `IEcsModuleSystem` in the `PostSimulation`
or `Export` phase; its `Update()` calls `_historyService.Capture(_eventBus)`.

The subsystem bootstrappers to update: locate SimHost, CGF, IG, ExCon subsystem bootstrapper
files in the `Hrot/Subsystems/` directory tree.

**Constraints:**
- The panel's existing rendering (frame + short type name, colour-coding, single-item
  copy-to-JSON) must be fully preserved.
- The `EventHistoryCaptureSystem` must be registered at `PostSimulation` or `Export` phase,
  NOT at a pre-simulation phase.

**Tests required:**
- `EventBrowserPanel` constructed with a mock `IDiagnosticEventHistoryService` returning 5
  events renders exactly 5 rows without exceptions (headless test)
- No compile errors across subsystems

---

### Task 3: IArchitectureDiagnosticsService (DD-P2-T03)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p2-t03--iarchitecturediagnosticsservice)

Create `FDP/Engine/Fdp.ModuleHost/Diagnostics/IArchitectureDiagnosticsService.cs`:
- `ArchitectureSnapshotDto` record with `IReadOnlyList<ModuleDiagnosticsDto> Modules`
  and `IReadOnlyList<TranslatorDiagnosticsDto> Translators`
- `interface IArchitectureDiagnosticsService { ArchitectureSnapshotDto GetSnapshot(); }`

Create `FDP/Engine/Fdp.ModuleHost/Diagnostics/ArchitectureDiagnosticsService.cs`:
- Constructor takes `ModuleHostKernel`
- `GetSnapshot()` extracts the same data that `ArchitectureDiagnosticsPanel.DrawContent()` 
  currently reads directly from `kernel`; move that reflection/LINQ logic here

Inject `IArchitectureDiagnosticsService` into `ArchitectureDiagnosticsPanel`, removing direct
`ModuleHostKernel` parameter from `DrawContent()`.

**Namespace:** `Fdp.ModuleHost.Diagnostics`

**Tests required:**
- `GetSnapshot()` with a kernel hosting 2 modules returns `Modules.Count == 2`
- `Snapshot.Translators` non-empty when kernel has translator-bearing systems

---

### Task 4: IEntityStateExtractionService (DD-P2-T04)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p2-t04--ientitystateextractionservice)

Create `FDP/Toolkits/Fdp.Toolkits/Diagnostics/IEntityStateExtractionService.cs`:

```csharp
namespace Fdp.Toolkit.Diagnostics;

public record EntityStateDumpDto(long NetworkId, string Json);

public interface IEntityStateExtractionService
{
    List<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? ids = null);
}
```

Create `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EntityStateExtractionService.cs`:
- Constructor: `EntityRepository repository, NetworkEntityMap networkEntityMap` (or equivalent
  types — examine `EntityJsonDumper.cs` and `EntityInspectorPanel.cs` to find the exact types)
- `ExtractEntities(null)`: dumps all entities that have a `NetworkIdentity` component
- `ExtractEntities(ids)`: dumps only entities whose `NetworkIdentity.Value` is in `ids`
- Uses `EntityJsonDumper.Dump` internally for the JSON serialisation

**Namespace:** `Fdp.Toolkit.Diagnostics`

**Tests required:**
- 3 entities with NetworkIdentity → `ExtractEntities(null)` returns 3
- `ExtractEntities(new[] { 4001L })` returns only the entity with NetworkIdentity.Value == 4001
- Entity without NetworkIdentity excluded from `ExtractEntities(null)`

---

### Task 5: ILogArchiveExtractionService (DD-P2-T05)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p2-t05--ilogarchiveextractionservice)

Create `Hrot/Engine/Hrot.Core/Diagnostics/ILogArchiveExtractionService.cs`:

```csharp
namespace Hrot.Core.Diagnostics;

public interface ILogArchiveExtractionService
{
    Task ExtractLogsAsync(
        string outputFilePath,
        DateTime cutoffTime,
        string severityThreshold,
        CancellationToken cancellationToken = default);
}
```

Create `Hrot/Engine/Hrot.Core/Diagnostics/LogArchiveExtractionService.cs`:
- Constructor: `HrotNodeConfig config` (for `LogDirectory` and `SubsystemName`, `NodeId`)
- File discovery pattern: `{SubsystemName}_{NodeId}*.log` in `config.LogDirectory`
- Open log files with `FileShare.ReadWrite`
- Filter lines using `ReadOnlySpan<char>` bracket token parsing (NOT `string.Split`)
- Filter by: (a) parsed timestamp vs `cutoffTime`, (b) parsed severity vs `severityThreshold`
- Write filtered lines to `outputFilePath` using `StreamWriter`
- When `LogDirectory` is empty: return immediately without creating output file or throwing

**Namespace:** `Hrot.Core.Diagnostics`

**NOTE:** `HrotNodeConfig.LogDirectory` is added in Phase 5 (DD-P5-T03). For this batch,
assume the property exists (it will be added as a simple `public string LogDirectory { get; set; }
= string.Empty;` to `Hrot/Engine/Hrot.Core/Infrastructure/HrotNodeConfig.cs`). Add it now
as part of this batch to make DD-P2-T05 compile.

**Tests required:**
- 10-line temp log file, 5 lines above severity threshold → output contains 5 lines
- Lines older than `cutoffTime` excluded
- File opened with `FileShare.ReadWrite` does not throw `IOException`
- `CancellationToken` cancellation stops processing without throwing (only
  `OperationCanceledException` acceptable)
- Empty `LogDirectory` → returns immediately, no output file created, no exception

---

## Mandatory Workflow

**CRITICAL: Complete tasks in order with passing tests before moving on.**

1. DD-P2-T01 → build and test pass
2. DD-P2-T02 → build and test pass (verify EventBrowserPanel panel tests)
3. DD-P2-T03 → build and test pass
4. DD-P2-T04 → build and test pass
5. DD-P2-T05 → build and test pass (including the empty LogDirectory guard)
6. Full solution build → verify no regressions

Do NOT stop to ask for permission. Fix all errors and test failures as you go. Write the
report only after everything passes.

---

## Testing Requirements

- Minimum 15 unit tests total across all tasks
- Thread-safety tests for DD-P2-T01 (concurrent read + write)
- Snapshot stability test (mutate after `GetHistory()`, verify returned array unchanged)
- Streaming / cancellation test for DD-P2-T05
- All existing tests continue to pass

---

## Quality Standards

**TEST QUALITY:**
- Thread-safety tests must actually use two threads (not just assert lock exists)
- Snapshot stability test must verify the returned array contents do not change after the
  call returns, not just that it is a new object reference
- DD-P2-T05 severity threshold test must count lines in the output file, not just assert
  the file exists

**SERVICES must be headless:** No using directives referencing ImGui, ImGuiNET, Raylib, or
any rendering namespace are permitted in the new service files.

---

## Success Criteria

- [ ] DD-P2-T01: `IDiagnosticEventHistoryService` and `DiagnosticEventHistoryService` in
      `Fdp.Core.Diagnostics`, circular buffer cap 500, copy-under-lock, filter by prefix
- [ ] DD-P2-T02: `EventBrowserPanel` injects the service, private capture loop removed,
      `EventHistoryCaptureSystem` registered in PostSimulation/Export phase in all subsystems
- [ ] DD-P2-T03: `IArchitectureDiagnosticsService` in `Fdp.ModuleHost.Diagnostics`,
      `ArchitectureDiagnosticsPanel` injects it
- [ ] DD-P2-T04: `IEntityStateExtractionService` in `Fdp.Toolkit.Diagnostics`,
      NetworkIdentity filter works
- [ ] DD-P2-T05: `ILogArchiveExtractionService` in `Hrot.Core.Diagnostics`, streaming async,
      `ReadOnlySpan<char>` parsing, FileShare.ReadWrite, empty LogDirectory guard
- [ ] `HrotNodeConfig.LogDirectory` property added
- [ ] All tests pass, no regressions
- [ ] Report submitted at `.dev/dump-diag/reports/BATCH-02-REPORT.md`

---

## Developer Insights (Report Questions)

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** For DD-P2-T02 (EventBrowserPanel refactor): how did you identify and remove all code
from the private capture loop without breaking the rendering? Were there subtle dependencies?

**Q3:** For DD-P2-T05 (LogArchiveExtractionService): how did you implement the
`ReadOnlySpan<char>` bracket parsing without allocating? Show the key span-based line.

**Q4:** What weak points did you spot in the existing codebase?

**Q5:** Any edge cases discovered beyond the spec?

---

## Reference Materials

- **Task Detail:** `.dev/dump-diag/TASK-DETAIL.md` — DD-P2-T01 through DD-P2-T05
- **Design:** `.dev/dump-diag/DESIGN.md` — Sections 2.1, 2.2, 2.3, 2.4
- **Debt Tracker:** `.dev/dump-diag/DEBT-TRACKER.md`
- **Phase 1 result:** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`
