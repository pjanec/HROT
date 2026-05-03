# BATCH-04-REPORT — Dump Diagnostics (DD-P5 Remaining + DD-P7 File Dialog)

**Status:** COMPLETE — All tasks implemented, all affected projects build with 0 errors.

---

## Task Completion Summary

| Task ID    | Description                                              | Status |
|------------|----------------------------------------------------------|--------|
| DD-P5-T01  | NLog file target with auto-rotation in ClusterRunner     | DONE   |
| DD-P5-T02  | `--log-dir` CLI option in HrotRunnerConfiguration       | DONE   |
| DD-P5-T04  | DiagnosticsDumpClusterOpHandler + NodeBootstrapper wire  | DONE   |
| DD-P5-T05  | NasBasePath, OrchestratorSubsystem wiring, LocalTempRoot | DONE   |
| DD-P7-T01  | IFileDialogService interface                             | DONE   |
| DD-P7-T02  | ImGuiFileDialogService implementation                   | DONE   |
| DD-P7-T03  | WindowManager.SetFileDialogService / per-frame Draw     | DONE   |

---

## Files Modified / Created

### DD-P5-T01 — NLog file target with auto-rotation
- `Hrot/Runner/Hrot.ClusterRunner/Program.cs`
  - Removed early `LogManager.Configuration = logConfig;` (was before file target setup).
  - Added file target setup block after config validation / "Starting" log line:
    resolves log directory from `config.LogDirectory` (or falls back to `<AppBase>\logs`),
    creates directory, sets MDLC `nodeId`, builds `FileTarget` with 50 MB rolling archives
    (max 10 files), appends the rule, then calls `LogManager.Configuration = logConfig;`.

### DD-P5-T02 — `--log-dir` CLI option
- `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`
  - Added `LogDirectory` string property with `[Option("log-dir", Required = false, ...)]`.
  - Default is `string.Empty` (runner falls back to `<AppBase>\logs`).

### DD-P5-T04 — DiagnosticsDumpClusterOpHandler + wiring
- `Hrot/Engine/Hrot.Common/Diagnostics/DiagnosticsDumpClusterOpHandler.cs` — NEW FILE
  - Namespace `Hrot.Common.Diagnostics`.
  - Implements `IClusterStateHandler` (newer 2PC interface).
  - `CanHandle`: returns true for `NodeOpType.CollectDiagnostics`.
  - `PrepareAsync`: deserializes `DiagnosticDumpPayloadDto` from `intent.DomainPayload`;
    checks `TargetNodeIds` filter; collects entities / architecture / events / logs into
    `LocalTempRoot/dumps/{transactionId:N}/`; returns `List<FileManifestEntry>`.
  - `AbortAsync`: deletes output directory if it was created.
- `Hrot/Engine/Hrot.Common/Hrot.Common.csproj`
  - Added `ProjectReference` to `Fdp.ModuleHost` (for `IArchitectureDiagnosticsService`).
  - Added `ProjectReference` to `Fdp.Toolkits` (for `IEntityStateExtractionService`,
    `JsonAestheticFormatter`).
- `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`
  - Added optional `Hrot.Common.Diagnostics.DiagnosticsDumpClusterOpHandler? diagnosticsDumpHandler = null`
    parameter to `BuildOrchestration`.
  - Registers handler on `clusterSlave` if non-null.
- `Hrot/Network/Hrot.Network.Orchestration/NodeOpSlaveTranslator.cs`
  - Added `case NedNodeOpType.CollectDiagnostics:` in `DeserializeNodePayload` switch,
    deserializing `DiagnosticDumpPayloadDto` via `JsonSerializer`.

### DD-P5-T05 — NasBasePath, OrchestratorSubsystem, LocalTempRoot
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterConfiguration.cs`
  - Added `NasBasePath` string property (`init`; defaults to `@"C:\FDP_Temp\shared"`).
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`
  - Added `_diagnosticsDumpProcessManager` field.
  - Replaced all three occurrences of `OrchestrationConstants.DefaultStagingDirectory`
    with `_config.NasBasePath` (StorageProcessManager, AssetInventoryProcessManager,
    AssetPrefetchProcessManager).
  - Registered `DiagnosticsConsensusAggregator` on `_clusterMaster`.
  - Constructed `DiagnosticsDumpProcessManager` with `_config.NasBasePath`.
  - Added `.Tick()` call in `Update`.
  - Added null in `Shutdown`.
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
  - `LocalTempRoot` now appended with `nodes/node-{localNodeId}` subfolder so each node
    writes diagnostics to its own isolated directory.

### DD-P7-T01 — IFileDialogService interface
- `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IFileDialogService.cs` — NEW FILE
  - Namespace `Fdp.Presentation.Abstractions`.
  - Single method: `Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter)`.

### DD-P7-T02 — ImGuiFileDialogService
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/ImGuiFileDialogService.cs` — NEW FILE
  - Namespace `Fdp.Presentation.Panels`.
  - Implements `IFileDialogService`.
  - State machine: `ShowSaveAsDialogAsync` stores `TaskCompletionSource<string?>`, cancels
    any pending previous dialog.
  - `Draw()` called each frame; renders `BeginPopupModal("Save As##FileDialog")` with directory
    navigator (dirs and filtered files), file-name input, Save/Cancel buttons.
  - Resolves TCS with full path on Save, null on Cancel/X.

### DD-P7-T03 — WindowManager wiring
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`
  - Added `using Fdp.Presentation.Abstractions;` and `using Fdp.Presentation.Panels;`.
  - Added `_fileDialogService` field (`IFileDialogService?`).
  - Added `SetFileDialogService(IFileDialogService)` public method.
  - At end of `Render()`: calls `(_fileDialogService as ImGuiFileDialogService)?.Draw()`
    so the modal renders on top of all other windows.

---

## Build Verification

All affected projects build with 0 errors:

| Project                                  | Result       |
|------------------------------------------|--------------|
| `Hrot.Common`                            | Build succeeded |
| `Hrot.SimHost`                           | Build succeeded |
| `Hrot.Orchestrator`                      | Build succeeded |
| `Hrot.ClusterRunner`                     | Build succeeded |
| `Fdp.Presentation`                       | Build succeeded |

---

## Implementation Notes

- `MappedDiagnosticsLogicalContext` (NLog 4 API) produces two pre-existing CS0618 obsolete
  warnings in ClusterRunner. These are consistent with the existing codebase style and do not
  affect correctness.
- `IEntityStateExtractionService.ExtractEntities(null)` passes null to extract all network-
  identifiable entities (the DTO has no per-entity network ID filter field).
- `DiagnosticsDumpClusterOpHandler` uses `intent.DomainPayload as DiagnosticDumpPayloadDto`
  because `IClusterStateHandler.ExecuteNodeOpIntent` carries a typed `DomainPayload`, not a raw
  JSON string. The `NodeOpSlaveTranslator.DeserializeNodePayload` case added for `CollectDiagnostics`
  ensures the payload is deserialized before the intent reaches the handler.
