# BATCH-05 Report — Diagnostic Dump: Merge Worker + ClusterDiagnosticsPanel + DiagnosticsWindow

**Date:** 2026-05-03  
**Status:** COMPLETE — all tasks implemented and verified (0 errors, all tests pass)

---

## Tasks Completed

| Task ID   | Description                                          | Status   |
|-----------|------------------------------------------------------|----------|
| DD-P8-T02 | Create `Events/DiagnosticsMergeEvents.cs`            | DONE     |
| DD-P8-T01 | Create `DiagnosticLogMergeWorker.cs`                 | DONE     |
| DD-P6-T01 | Create `Panels/ClusterDiagnosticsPanel.cs` (config + execute) | DONE |
| DD-P6-T02 | Extend `ClusterDiagnosticsPanel.cs` (results tree + context menus) | DONE |
| DD-P8-T03 | Extend `ClusterDiagnosticsPanel.cs` (Merge Logs button + cluster aggregates) | DONE |
| DD-P6-T03 | Create `Windows/DiagnosticsWindow.cs`, register in OrchestratorSubsystem and ExConSubsystem | DONE |

---

## Files Created

### `Hrot\Subsystems\Hrot.Orchestrator\Events\DiagnosticsMergeEvents.cs`
Local-bus-only event structs for the diagnostic log merge feature.

- `MergeLogsIntent` (`[EventId(9059)]`, `DataPolicy.NoRecord`):  
  - `string[] LogRelativePaths` — relative paths under NAS base path  
  - `string NasBasePath`  
  - `string DumpTimestamp`  
- `LogMergeCompletedEvent` (`[EventId(9060)]`, `DataPolicy.NoRecord`):  
  - `string NasPath` — absolute path to the merged output file  

### `Hrot\Subsystems\Hrot.Orchestrator\DiagnosticLogMergeWorker.cs`
K-way chronological merge of per-node diagnostic log files.

- Constructor: `DiagnosticLogMergeWorker(FdpEventBus bus)`
- `Tick()`: reads `MergeLogsIntent` from bus CURRENT, cancels any prior `CancellationTokenSource`, spawns `Task.Factory.StartNew(DoMerge, ..., LongRunning)`
- `DoMerge(MergeLogsIntent, CancellationToken)`: opens `StreamReader`s (missing files skipped with a warning), calls `MergeReadersCore`, publishes `LogMergeCompletedEvent` unless cancelled
- `internal static void MergeReadersCore(IEnumerable<TextReader>, TextWriter, CancellationToken)`: K-way merge using `PriorityQueue<(string Line, TextReader Reader), DateTime>`; continuation lines (no timestamp prefix) follow their originating entry inline
- `TryParseTimestamp(ReadOnlySpan<char>, out DateTime)`: parses `[YYYY-MM-DD HH:mm:ss.ffff]` and `[YYYY-MM-DD HH:mm:ss.fff]` prefixes
- Output path: `<nasBasePath>/dumps/dump_<timestamp>_logs_MERGED.log`
- `Dispose()`: cancels active CTS and disposes it

### `Hrot\Subsystems\Hrot.Orchestrator\Panels\ClusterDiagnosticsPanel.cs`
Full-featured diagnostics ImGui panel.

**Section 1 — Configuration:**
- Node multi-select checkboxes (All / individual, sourced from `_uiCache.ActiveNodes`)
- Provider multi-select (EventLog, Serilog, NLog, Custom)
- Dump flags: DumpEvents, DumpEntities, DumpArchitecture, DumpLogs (checkboxes)
- MaxAgeHours slider (1–168), SeverityThreshold combo (Trace/Debug/Info/Warning/Error/Critical)
- UseMarkdownWrapper checkbox
- Custom providers textbox (comma-separated)

**Section 2 — Execute:**
- `[Request Dump]` button: publishes `ExecuteDiagnosticDumpIntent` with `Guid.NewGuid()` as TransactionId, NodeIds from selection, providers from textbox
- Status line showing last intent TxId

**Section 3 — Results Tree:**
- Tree nodes per completed `DiagnosticDumpCompletedEvent` from `_uiCache.TxHistory`
- Shows: timestamp, success/fail badge, node count, file count, dump size estimate
- Per-entry file list with NAS path column (right-click to copy UNC path to clipboard)
- Clipboard copy uses `volatile string? _pendingClipboardText` pattern (set in render, applied next frame via `ImGui.SetClipboardText()`)

**Section 4 — Merge Logs (DD-P8-T03):**
- Shown only when `_uiCache.LastDumpManifest` is non-empty
- Lists all `.log` files from the manifest
- `[Merge Logs]` button: publishes `MergeLogsIntent` with NasBasePath and DumpTimestamp extracted from manifest paths
- Shows last merge output path when `LogMergeCompletedEvent` received

**Section 5 — Cluster Aggregates (DD-P8-T03):**
- Table of all active nodes: NodeId, SubsystemName, LocalClusterState, WallTicksUtc age
- Row is highlighted red if heartbeat age > 5 seconds

### `Hrot\Subsystems\Hrot.Orchestrator\Windows\DiagnosticsWindow.cs`
Thin `ManagedWindow` wrapper around `ClusterDiagnosticsPanel`.

- Title: `"Diagnostics"`, scope: `WindowScope.Global`, `IsOpen = true` at startup
- Delegates `DrawContent()` to `_panel.Draw()`
- Visibility: `public` (accessible from ExCon assembly)

### `Hrot\Subsystems\Hrot.Orchestrator.Tests\DiagnosticLogMergeWorkerTests.cs`
4 unit tests for `DiagnosticLogMergeWorker`:

| Test | Description |
|------|-------------|
| `MergeReadersCore_ThreeStreams_OutputIsChronological` | Three interleaved readers produce chronologically ordered output |
| `MergeReadersCore_StackTrace_AppearsAfterOriginatingEntry` | Continuation lines (stack trace) are not interleaved with later-timestamp entries |
| `Tick_InaccessibleFile_SkippedAndRemainingMerge` | Missing file path is skipped; valid files still produce merged output |
| `Tick_CancellationToken_NoEventPublished` | `Dispose()` does not throw and cancels the active merge task |

---

## Files Modified

### `Hrot\Subsystems\Hrot.Orchestrator\Panels\ClusterUiCache.cs`
- Added `using Hrot.Network.Orchestration;`
- Added `public FileManifestEntry[] LastDumpManifest { get; private set; } = Array.Empty<FileManifestEntry>();`
- In `DrainSysOpStatus()`: on success with `ResultPayload is List<FileManifestEntry>`, strips `SourceUnc` and stores manifest

### `Hrot\Subsystems\Hrot.Orchestrator\OrchestratorSubsystem.cs`
- Added fields: `_mergeWorker`, `_fileDialogService`, `_diagnosticsPanel`
- `Initialize()`: constructs `DiagnosticLogMergeWorker`, `ImGuiFileDialogService`, `ClusterDiagnosticsPanel`
- `Update()`: calls `_mergeWorker?.Tick()`
- `RegisterWindows()`: calls `windowManager.SetFileDialogService(_fileDialogService)` and registers `DiagnosticsWindow`
- `Shutdown()`: disposes and nulls `_mergeWorker`, `_diagnosticsPanel`, `_fileDialogService`

### `Hrot\Subsystems\Hrot.ExCon\ExConSubsystem.cs`
- Added fields: `_clusterDiagnosticsPanel`, `_exConFileDialogService`
- `Initialize()`: constructs `ImGuiFileDialogService` and `ClusterDiagnosticsPanel` (with `nasBasePath: string.Empty`)
- `RegisterWindows()`: calls `windowManager.SetFileDialogService(_exConFileDialogService)` and registers `DiagnosticsWindow`
- `Shutdown()`: nulls `_clusterDiagnosticsPanel`, `_exConFileDialogService`

---

## Build & Test Results

```
Hrot.Orchestrator   : Build succeeded — 0 errors, 4 warnings (pre-existing)
Hrot.ExCon          : Build succeeded — 0 errors, 12 warnings (pre-existing)
Hrot.Orchestrator.Tests : Passed! — Failed: 0, Passed: 4, Skipped: 0 (Duration: 414ms)
```

---

## Design Decisions

1. **`DiagnosticsWindow` is `public`** — OrchestratorWindow is in Hrot.Orchestrator; ExCon needs cross-assembly access. Since ExCon references Hrot.Orchestrator, making the window class `public` is the minimal change.

2. **ExCon nasBasePath = `string.Empty`** — ExCon does not load `ClusterConfiguration`; it has no NAS path context. The merge feature in ExCon will be inert until ExCon receives a proper path via future config.

3. **`ImGuiFileDialogService` per-subsystem** — Each subsystem registers its own `ImGuiFileDialogService` instance via `WindowManager.SetFileDialogService()`. This follows the existing pattern and ensures no shared state between Orchestrator and ExCon when running in the same process.

4. **`ClusterDiagnosticsPanel` has no direct DDS imports** — all node data flows through `ClusterUiCache` using `var` inference per the project's CQRS rule.

5. **K-way merge uses `PriorityQueue<(string Line, TextReader Reader), DateTime>`** — avoids reading all lines into memory; each stream contributes one entry to the heap at a time. Continuation lines (non-timestamped) are drained inline before the next entry is enqueued.
