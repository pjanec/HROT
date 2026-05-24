# Onboarding — Cluster-Wide Diagnostic Dump

## Project Overview

This workstream adds a cluster-wide diagnostic snapshot capability to the Hrot simulation
platform. Operators can trigger a single dump command from ExCon (or the Orchestrator UI) that
causes every selected node to gather its local entity state, event history, architecture profile,
and NLog log files. The node-generated files are staged locally and then pulled to the central NAS
via the existing SMB Pull Gateway. The results are displayed in a new Diagnostics panel with
context menus for viewing, copying, and saving the dump files locally.

The workstream also introduces multi-select copy-to-JSON in the Event Browser and Entity Inspector
panels, and consolidates all JSON serialisation configuration into a central registry to fix a
`FixedString64` serialisation bug and eliminate duplicated `JsonSerializerOptions` instances
scattered across the codebase.

---

## Planning Artifacts

| Document | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Phased architecture — WHAT and WHY for every change |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task scope, constraints, and success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

---

## Folder Layout

### New files to create

| Path | Description |
|---|---|
| `FDP/Engine/Fdp.Core/Serialization/Converters/` | FixedString32/64Converter, VectorArrayConverters (moved from Fdp.Toolkits) |
| `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs` | Central JSON options registry |
| `FDP/Engine/Fdp.Core/Diagnostics/IDiagnosticEventHistoryService.cs` | Event history service interface + CapturedEventDto |
| `FDP/Engine/Fdp.Core/Diagnostics/DiagnosticEventHistoryService.cs` | Headless event history implementation |
| `FDP/Engine/Fdp.ModuleHost/Diagnostics/IArchitectureDiagnosticsService.cs` | Architecture snapshot interface + DTOs |
| `FDP/Engine/Fdp.ModuleHost/Diagnostics/ArchitectureDiagnosticsService.cs` | Architecture snapshot implementation |
| `FDP/Engine/Fdp.Presentation/Abstractions/IFileDialogService.cs` | Save-As dialog contract |
| `FDP/Engine/Fdp.Presentation/ImGui/Services/ImGuiFileDialogService.cs` | ImGui Save-As dialog implementation |
| `FDP/Toolkits/Fdp.Toolkits/Serialization/JsonAestheticFormatter.cs` | Numeric array flattening utility |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/IEntityStateExtractionService.cs` | Entity dump interface + EntityStateDumpDto |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EntityStateExtractionService.cs` | Entity dump implementation |
| `Hrot/Engine/Hrot.Core/Diagnostics/ILogArchiveExtractionService.cs` | Log archive streaming interface |
| `Hrot/Engine/Hrot.Core/Diagnostics/LogArchiveExtractionService.cs` | Log archive streaming implementation |
| `Hrot/Engine/Hrot.Common/Diagnostics/DiagnosticsDumpClusterOpHandler.cs` | Node-side 2PC dump handler |
| `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticsConsensusAggregator.cs` | Node response aggregator for dump op |
| `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticsDumpProcessManager.cs` | NAS pull trigger for dump files |
| `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterDiagnosticsPanel.cs` | Diagnostics UI panel |
| `Hrot/Subsystems/Hrot.Orchestrator/Events/DiagnosticsMergeEvents.cs` | MergeLogsIntent + LogMergeCompletedEvent |
| `Hrot/Subsystems/Hrot.Orchestrator/DiagnosticLogMergeWorker.cs` | K-way log merge worker |

### Key existing files touched

| Path | What changes |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioJsonConverters.cs` | Add forwarders for moved converters (including `StrictStringEnumConverter`) |
| `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationJsonOptions.cs` | Retain thin wrapper; `StrictStringEnumConverter` type moved to `Fdp.Core` |
| `FDP/Engine/Fdp.Core/FlightRecorder/FdpAutoSerializer.cs` | Replace `_fieldAwareOptions` with registry |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` | Same as above |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs` | Delegate to `JsonAestheticFormatter` |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/EventBrowserPanel.cs` | Multi-select, refactored to use service |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/ArchitectureDiagnosticsPanel.cs` | Refactored to use service |
| `FDP/Engine/Fdp.Presentation/ImGui/Utils/EntityJsonDumper.cs` | Use registry + formatter |
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` | Add `IFileDialogService.Draw()` call |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/ClusterOpType.cs` | Add `DumpDiagnostics = 16` |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/NodeOpType.cs` | Add `DumpDiagnostics = 28` |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterOpIntents.cs` | Add `ExecuteDiagnosticDumpIntent` |
| `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs` | Add `DiagnosticDumpPayloadDto` |
| `Hrot/Network/Hrot.Network.Orchestration/ClusterOpEgressTranslator.cs` | Handle `DumpDiagnostics` case |
| `Hrot/Network/Hrot.Network.Orchestration/ClusterOpMasterTranslator.cs` | Handle `DumpDiagnostics` case |
| `Hrot/Engine/Hrot.Core/Infrastructure/HrotNodeConfig.cs` | Add `LogDirectory` property |
| `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` | Add `--log-dir` option |
| `Hrot/Runner/Hrot.ClusterRunner/Program.cs` | Add NLog `FileTarget` with standardised layout and `{SubsystemName}_{NodeId}` filename |
| Various subsystem bootstrappers | Register new services and handler; namespace `LocalTempRoot` by node ID |
| `ClusterConfiguration.cs` (Hrot.Orchestrator) | Add `NasBasePath` property; `OrchestratorSubsystem` wires it to all process managers |

---

## Build and Run

Build everything from the FDP solution root:
```
cd FDP
dotnet build FDP.sln
```

Run tests:
```
dotnet test FDP.sln
```

Or run cluster-runner in all-subsystems mode:
```
cd Hrot/Runner/Hrot.ClusterRunner
dotnet run -- --mode all --log-dir C:\FDP_Logs
```

---

## Developer Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` to understand the batch-based development
workflow used in this repository before starting any implementation.

The typical flow is:
1. Receive a batch instruction file from the dev lead.
2. Implement the tasks described in it, using TASK-DETAIL.md as the specification.
3. Verify all success conditions (unit tests, compile tests) pass.
4. Write a batch report.
5. The dev lead reviews and approves before the next batch is issued.
