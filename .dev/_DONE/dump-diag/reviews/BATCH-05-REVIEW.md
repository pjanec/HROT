# BATCH-05 Review

**Batch:** BATCH-05
**Tasks:** DD-P6-T01, DD-P6-T02, DD-P6-T03, DD-P8-T01, DD-P8-T02, DD-P8-T03
**Build:** Hrot.Orchestrator 0 errors / 4 warnings (pre-existing), Hrot.ExCon 0 errors / 12 warnings (pre-existing)
**Tests:** 131/131 pass (+4 new DiagnosticLogMergeWorkerTests)
**Verdict:** APPROVED

---

## Review Summary

BATCH-05 completes the entire diagnostic dump feature. All six remaining tasks are implemented,
all builds are clean, and the test suite grew from 127 to 131 passing tests with zero regressions.

---

## Per-Task Assessment

### DD-P8-T02 — MergeLogsIntent and LogMergeCompletedEvent ✅

`Events/DiagnosticsMergeEvents.cs` correctly defines both event structs as
`[EventId] + [DataPolicy(DataPolicy.NoRecord)]`. EventIds 9059/9060 are the next available
IDs after 9058 (`ExecuteDiagnosticDumpIntent`). Both structs have `{ get; init; }` properties
consistent with the immutable value pattern used throughout the codebase.

### DD-P8-T01 — DiagnosticLogMergeWorker ✅

The K-way merge implementation is correct and idiomatic:

- `PriorityQueue<(string Line, TextReader Reader), DateTime>` — proper heap usage
- `TryParseTimestamp` uses `ReadOnlySpan<char>` + `DateTime.TryParseExact` with span overload —
  no per-line string allocation
- Continuation lines are drained inline before enqueuing the next timestamped line from the same
  reader — correct ordering preservation for stack traces
- Missing files write a warning to the output rather than throwing
- `Tick()` drains `ReadManaged<MergeLogsIntent>()` so it integrates cleanly with the existing bus loop
- `internal static MergeReadersCore(IEnumerable<TextReader>, TextWriter, CancellationToken)` —
  testable surface exposed correctly
- `Dispose()` cancels and disposes the active CTS — no resource leaks
- 4 unit tests cover: chronological ordering, stack trace continuation, inaccessible file skip,
  cancellation without event publication

### DD-P6-T01 — ClusterDiagnosticsPanel Configuration + Execution ✅

- CQRS read-side: `ClusterUiCache.ActiveNodes` → distinct `SubsystemName` → checkbox matrix
- `RenderExecuteButton()`: collects target node IDs from selected subsystems, builds
  `DiagnosticDumpPayloadDto`, serialises via `FdpJsonOptionsRegistry.DefaultRelaxed`,
  publishes `ExecuteDiagnosticDumpIntent` — exactly matches the spec
- No DDS type references in the file (confirmed by usings audit)
- `_subsystemSelected` dictionary lazily initialised on first render pass

### DD-P6-T02 — ClusterDiagnosticsPanel Results Tree + Context Menus ✅

- `SyncManifestFromCache()` uses `ReferenceEquals` to detect manifest changes — efficient,
  avoids unnecessary rebuilds
- `FlushClipboard()` polled at the top of `Render()` — correct thread-safety pattern:
  background tasks set `_pendingClipboardText`, render thread calls `ImGui.SetClipboardText`
- Four context menu operations implemented: Copy NAS Path, Copy Content (10 MB guard), Open
  from NAS (ProcessStartInfo + UseShellExecute), Save Local Copy As (async void + IFileDialogService)
- `BuildAggregatedJson()`: entity files merged to `{ "SubsystemName": [...] }`, event files
  merged to `{ "SubsystemName": { "Provider": [...] } }` — correct composite schema
- `_copyInProgress` guard prevents concurrent aggregation operations
- `_inlineError` displayed inline in the results section for user-visible error feedback

### DD-P8-T03 — Merged Log Entry in ClusterDiagnosticsPanel ✅

- "Generate Merged Cluster Log" button disabled when no `.log` files in manifest or merge
  in progress — correct disable guard
- `MergeLogsIntent` published with `LogRelativePaths`, `NasBasePath`, `DumpTimestamp` extracted
  from manifest
- `DrainLogMergeEvents()` reads `LogMergeCompletedEvent` from bus and sets `_mergedLogPath`
- "Cluster Aggregates" tree node with merged file entry uses the same `RenderFileEntry()`
  helper — consistent UX

### DD-P6-T03 — Register Panel in OrchestratorSubsystem and ExConSubsystem ✅

- `DiagnosticsWindow` is a correct `ManagedWindow` thin wrapper (matches OrchestratorWindow
  pattern exactly)
- `DiagnosticsWindow` is `public` — needed because ExCon instantiates it from a separate assembly
- `OrchestratorSubsystem`: fields `_mergeWorker`, `_fileDialogService`, `_diagnosticsPanel`
  wired in `Initialize()`, ticked in `Update()`, disposed in `Shutdown()`
- `ExConSubsystem`: panel registered with `nasBasePath: string.Empty` — acceptable deviation;
  ExCon has no `ClusterConfiguration` source; merge feature will be inert until wired
- `WindowManager.SetFileDialogService()` called before `RegisterWindow` — correct ordering

---

## Deviations from Spec

| Deviation | Justification | Risk |
|-----------|---------------|------|
| ExCon `nasBasePath = string.Empty` | ExCon does not load `ClusterConfiguration`; no NAS path available | Low — merge is inert in ExCon, not broken |
| `DiagnosticsWindow` is `public` | Cross-assembly access required from ExCon | Low — internal API, not external facing |
| EventId added to MergeLogsIntent/LogMergeCompletedEvent (spec said not needed) | Framework requires EventId for event bus registration | None — compatible addition |

---

## Pre-existing Issues (Not Regressions)

- 3 pre-existing failures in `EntityInspectorPanelTests.GetFilteredEntities_RespectsLimit`
  were NOT present this run (131/131 pass) — these may have been fixed in a prior batch or
  the test run this time had no issue
- 4 pre-existing RS2008 / CA2014 warnings in ExtDeps unchanged

---

## TASK-TRACKER Update

All tasks marked [x]. The diagnostic dump feature is now fully implemented across all phases:
- Phase 1 ✅, Phase 2 ✅, Phase 3 ✅, Phase 4 ✅, Phase 5 ✅, Phase 6 ✅, Phase 7 ✅, Phase 8 ✅
