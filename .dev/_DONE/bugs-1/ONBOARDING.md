# BUG1 — Onboarding Guide

Welcome to the **bugs-1** workstream. This document gives a new developer everything needed to
understand the work, find the relevant code, build the project, and start contributing.

---

## What Are We Fixing?

This workstream addresses a batch of bugs and small features discovered during interactive testing
of the IOS / IG / SimHost federated simulation stack. The issues fall into four areas:

| Area | Summary |
|---|---|
| **Infrastructure** | SimHost silently joins DDS Domain 42 instead of Domain 0; missing `--node-id` CLI flag; batch launch scripts use the wrong working directory |
| **Network correctness** | Non-authoritative nodes emit spurious ACKs on descriptor update requests; deleted entities leave orphaned DDS topic samples that haunt late-joining nodes |
| **IG feature** | No debug-panel toggle to send continuous (throttled) geospatial updates during entity drag for latency testing |
| **Mission system** | Entity stops at the first waypoint (empty trigger list falls back to `TimerElapsed(float.MaxValue)`); clicking ABORT then COMMIT triggers a false OCC version conflict |

One item (IOS context menu Delete action) was found to be **already implemented** in the codebase.

---

## Planning Documents

| Document | Location |
|---|---|
| Design (WHAT & WHY) | [docs/bugs-1/DESIGN.md](./DESIGN.md) |
| Task Detail (HOW + success conditions) | [docs/bugs-1/TASK-DETAIL.md](./TASK-DETAIL.md) |
| Task Tracker (progress checklist) | [docs/bugs-1/TASK-TRACKER.md](./TASK-TRACKER.md) |

Read DESIGN.md first to understand the reasoning, then look up the specific task in TASK-DETAIL.md
before touching any code.

---

## Folder Layout — Where Is The Code?

### Runner & Infrastructure

| Path | Relevance |
|---|---|
| `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` | DDS domain zero guard fix (BUG1-F001) |
| `Hrot.ClusterRunner/Services/IgSubsystem.cs` | Node-ID injection for IG (BUG1-F002) |
| `Hrot.ClusterRunner/Program.cs` | CLI parsing, maps config → `RunnerOptions` |
| `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` | Project-specific CLI options |
| `FDP/Framework/FDP.Framework.Runner/RunnerConfiguration.cs` | Base CLI options (add `--node-id` here) |
| `FDP/Framework/FDP.Framework.Runner/RunnerOptions.cs` | Runtime options struct (add `NodeId`) |
| `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs` | Per-subsystem config (add `NodeId`) |
| `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` | Bootstraps subsystems, apply ID offsets here |
| `run_all_standalone.bat`, `run_SimHost.bat`, `run_IG.bat`, `run_IOS.bat` | Batch launch scripts (`cd` fix) |

### Network Layer

| Path | Relevance |
|---|---|
| `Hrot.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` | Silent bystander fix (BUG1-N001) |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs` | Fan-out disposal (BUG1-N002) |

### IG — Image Generator

| Path | Relevance |
|---|---|
| `Hrot.IG/IgApplication.cs` | Main application — drag event subscriptions, `SendWorldPosUpdate` helper to add |
| `Hrot.IG/Systems/MapUserConfig.cs` | User config struct — add `ContinuousDragUpdates` bool |
| `Hrot.IG.Tests/` | Unit tests for IG |

### IOS — Operator System

| Path | Relevance |
|---|---|
| `Hrot.ExCon/Panels/MissionPanel.cs` | `HandleAddTask` (inject `DoctrineFinished`), `HandleAbort`/`HandleJump` (async fix) |
| `Hrot.ExCon/Services/MissionEditorService.cs` | Add `SendControlCommandAsync` |
| `Hrot.ExCon/Services/IMissionEditorService.cs` | Add method to interface |
| `Hrot.ExCon/Logic/ContextMenuLogic.cs` | Context menu strategies (Delete already in Standard — no change) |
| `Hrot.ExCon.Tests/` | Unit tests for IOS |

---

## Building the Project

```powershell
# Restore all NuGet packages
dotnet restore IOS-IG-SimHost.sln

# Build entire solution
dotnet build IOS-IG-SimHost.sln
```

Run the full test suite:

```powershell
dotnet test IOS-IG-SimHost.sln --no-restore -v q
```

Run tests for a specific project:

```powershell
dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj          --no-restore -v q
dotnet test Hrot.ExCon.Tests/Hrot.ExCon.Tests.csproj         --no-restore -v q
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-restore -v q
```

Launch all three subsystems in separate windows (after applying BUG1-F003):

```bat
run_all_standalone.bat
```

---

## Development Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` for the full batch-based development workflow used on
this project. In brief:

1. Pick a task from [TASK-TRACKER.md](./TASK-TRACKER.md).
2. Read its entry in [TASK-DETAIL.md](./TASK-DETAIL.md) and the linked DESIGN.md section.
3. Implement and write / update the unit tests described in the Success Conditions.
4. Run the relevant test project to confirm all pass.
5. Mark the task `[x]` in TASK-TRACKER.md and report your work in a batch report.
