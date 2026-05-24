# BATCH-03-REPORT — Dump Diagnostics (DD-P3 Multi-Select + DD-P4 Infrastructure)

**Status:** COMPLETE — All tasks implemented, all new tests passing.

---

## Task Completion Summary

| Task ID    | Description                                  | Status    |
|------------|----------------------------------------------|-----------|
| DD-P3-T01  | EventBrowserPanel multi-select               | DONE      |
| DD-P3-T02  | EntityInspectorPanel multi-select            | DONE      |
| DD-P4-T01  | DumpDiagnostics enums + DiagnosticDumpPayloadDto | DONE  |
| DD-P4-T02  | ExecuteDiagnosticDumpIntent struct           | DONE      |
| DD-P4-T03  | ClusterOpEgressTranslator + ClusterOpMasterTranslator DumpDiagnostics handling | DONE |
| DD-P4-T04  | DiagnosticsConsensusAggregator              | DONE      |
| DD-P4-T05  | DiagnosticsDumpProcessManager               | DONE      |

---

## Files Modified

### DD-P3-T01 — EventBrowserPanel Multi-Select
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/EventBrowserPanel.cs`
  - Replaced `_selectedEvent` (single) with `_selectedEvents: HashSet<CapturedEventDto>` + `_lastClickedIndex`
  - `DrawEventList()`: rebuilt with Ctrl/Shift multi-select logic via `HandleRowClick()`
  - `DrawEventDetails()`: 0 selected = hint; 1 = existing detail; N>1 = summary + "Copy JSON (N items)"
  - `HandleRowClick()` (internal): Ctrl=toggle, Shift=range, plain=clear+add
  - `BuildCopyJson()` (internal static): single → object; multiple → array sorted by Frame
- `FDP/Engine/Fdp.Presentation.Tests/ImGui/EventBrowserPanelTests.cs`
  - 5 new tests: CtrlClick_TwoRows_SelectsBoth, ShiftClick_Range, BuildCopyJson (3 variants)

### DD-P3-T02 — EntityInspectorPanel Multi-Select
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityInspectorPanel.cs`
  - Added `_selectedEntities: HashSet<Entity>`, `_lastClickedIndex`, `_extractionService`
  - Optional constructor `EntityInspectorPanel(IEntityStateExtractionService? extractionService = null)`
  - `DrawEntityList()`: indexed for-loop with Ctrl/Shift detection; multi-entity context menu item
  - `HandleRowClick()` (internal): same pattern as EventBrowserPanel
  - `BuildMultiEntityJson()`: uses extractionService.ExtractEntities
  - `DrawEntityDetails()`: shows "Multiple entities selected (N) - details not available" for N>1
- `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IEntityContextMenuHandler.cs`
  - Added default interface method: `void PopulateMenu(IReadOnlyCollection<Entity> entities, IContextMenuBuilder builder) { }`
- `FDP/Engine/Fdp.Presentation.Tests/ImGui/EntityInspectorPanelTests.cs`
  - `EntityInspectorPanelMultiSelectTests` class with 4 tests

### DD-P4-T01 — Enums + DTO
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/ClusterOpType.cs` — `DumpDiagnostics = 16`
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/NodeOpType.cs` — `CollectDiagnostics = 28` (NOT DumpDiagnostics — IDL collision avoidance)
- `Hrot/Network/Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs` — same values in DDS enums
- `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs` — `DiagnosticDumpPayloadDto` record

### DD-P4-T02 — Intent struct
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterOpIntents.cs` — `ExecuteDiagnosticDumpIntent` struct (EventId 9058)

### DD-P4-T03 — Translator handling
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterOpEgressTranslator.cs` — `DumpDiagnostics` case dispatches `CollectDiagnostics` NodeOps
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterOpMasterTranslator.cs` — `DumpDiagnostics` case publishes `ExecuteDiagnosticDumpIntent`, registers `DiagnosticsConsensusAggregator`

### DD-P4-T04 — DiagnosticsConsensusAggregator
- `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticsConsensusAggregator.cs` (new file)
  - `TargetOp = NodeOpType.CollectDiagnostics`
  - `Aggregate()`: deserializes per-node JSON manifest, stores full manifest internally, returns stripped manifest (SourceUnc cleared)
  - `TakeFullManifest()`: drains and returns the internally stored full manifest (one-shot)
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/DiagnosticsConsensusAggregatorTests.cs` (new, 6 tests)

### DD-P4-T05 — DiagnosticsDumpProcessManager
- `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticsDumpProcessManager.cs` (new file)
  - Tracks `ExecuteDiagnosticDumpIntent` to build `_pendingDumpRequestIds`
  - On success: calls `aggregator.TakeFullManifest()`, starts `PullToNasAsync`
  - ContinueWith: checks `FailureCount == 0` (not just `IsCompletedSuccessfully`) for accurate failure detection
  - On abort/rejection: immediately publishes Failure event
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/DiagnosticsDumpProcessManagerTests.cs` (new, 4 tests)

---

## Test Results

| Suite                                | Result             |
|--------------------------------------|--------------------|
| Hrot.Orchestrator.Tests (all 127)   | 127 passed, 0 failed |
| Fdp.Presentation.Tests (all 260)    | 257 passed, 3 failed (pre-existing GetFilteredEntities failures — not caused by this batch) |

---

## Issues Encountered

### 1. CycloneDDS IDL Enum Name Collision
DDS IDL enum values are MODULE-SCOPED (not enum-scoped). `DumpDiagnostics` in both `ClusterOpType` and `NodeOpType` inside the same IDL module produces a compile error. Resolution: `NodeOpType` uses `CollectDiagnostics = 28` (semantically accurate — the nodes *collect* diagnostics, the cluster op *dumps* them).

### 2. PullToNasAsync Swallows Per-File Errors
`StorageGatewayModule.PullToNasAsync` catches all per-file exceptions internally and increments `failureCount`. This means the returned `Task<GatewayResult>` never faults from file-not-found — it completes successfully with `FailureCount > 0`. The `DiagnosticsDumpProcessManager` ContinueWith was updated to check `pullTask.Result.FailureCount == 0` in addition to `pullTask.IsCompletedSuccessfully`. The StorageProcessManager does not have this fix (it uses `IsFaulted` only) — recorded in debt tracker.

---

## Design Decisions Beyond Spec

1. **DiagnosticsConsensusAggregator.TakeFullManifest() is one-shot**: Returns and clears the internally stored full manifest. This prevents stale manifests from leaking across requests if a Tick cycle is missed.

2. **IEntityContextMenuHandler default interface method**: Used a default interface method (`void PopulateMenu(IReadOnlyCollection<Entity> ...)`) to preserve backward compatibility with all existing handler implementations rather than requiring them to add the method.

3. **EntityInspectorPanel constructor is additive**: The `IEntityStateExtractionService` is injected via an optional parameter in a new constructor overload; the default parameterless constructor still works.

---

## Weak Points Spotted

- `StorageProcessManager.ContinueWith` only checks `IsFaulted`, not `GatewayResult.FailureCount` — mirrors the same bug fixed in `DiagnosticsDumpProcessManager`. Should be recorded as tech debt.
- `EntityInspectorPanel.BuildMultiEntityJson` calls `ExtractEntities(null)` (passing null frame); if the extraction service strictly requires a frame, this could NRE. The spec doesn't define the frame parameter for multi-entity export.
- `HandleRowClick` in both panels passes the full `viewList` as a list, which is rebuilt each `DrawEventList`/`DrawEntityList` call. For large entity counts this is a per-frame allocation.
