# BUG2 — Onboarding Guide

Welcome to the **bugs-2** workstream. This document gives a new developer everything needed to
understand the work, find the relevant code, build the project, and start contributing.

---

## What Are We Fixing?

This workstream addresses a second batch of bugs and small features discovered during interactive
testing of the IOS / IG / SimHost federated simulation stack. The issues span nine areas:

| Area | Summary |
|---|---|
| **Network Correctness** | Duplicate ACKs from a double-registered system; all DDS participants missing `EnableSenderTracking`; WorldPos topic instance not tombstoned on entity deletion |
| **Mission System** | `BehaviorFinished` and `UnderAttack` triggers silently fall back to `TimerElapsed(0f)`, causing vehicles to skip their first task; no trigger editing UI in the IOS task editor; unreadable Unicode symbol buttons; no version-conflict resolution UI |
| **IOS UI Clean-up** | Legacy tool-selection combo still present in Map Configuration panel; ORBAT tree subordinates rendered without indentation |
| **IG Interaction** | No per-frame drag update mode; SHIFT key should trigger immediate DDS geo-spatial broadcast for testing |
| **Layer Visibility** | Entities on disabled layers remain selectable and show selection rings; entity render layer ignores per-entity layer masks |
| **Tool Cursors** | Measure tool and EntityPickerTool show no visual feedback while waiting for the operator's first click |
| **Entity Deletion** | Inspector context menus lack a networked Delete item; IOS DELETE context menu action is never executed on the IG |
| **Road Graph** | SimHost silently discards the loaded `RoadNetworkBlob` and uses a hardcoded relative path that breaks when running from the Runner's CWD |
| **Architecture (DEBT-033)** | `HealthData` in `Fdp.Kernel` is a documented mirror-hack; the clean fix is to move `Health` into the existing `FDP.Toolkit.Combat.Contracts` shared assembly |

---

## Planning Documents

| Document | Location |
|---|---|
| Design (WHAT & WHY) | [docs/bugs-2/BUG2-DESIGN.md](./BUG2-DESIGN.md) |
| Task Detail (HOW + success conditions) | [docs/bugs-2/BUG2-TASK-DETAIL.md](./BUG2-TASK-DETAIL.md) |
| Task Tracker (progress checklist) | [docs/bugs-2/BUG2-TASK-TRACKER.md](./BUG2-TASK-TRACKER.md) |

Read **BUG2-DESIGN.md** first to understand the reasoning behind each fix, then look up the
specific task in **BUG2-TASK-DETAIL.md** before writing any code.

---

## Developer Workflow

Read the **DEV-GUIDE.md** before starting work:

```
.dev-workstream/guides/DEV-GUIDE.md
```

It defines how batches are structured, how to write a batch report, and what "done" means.

---

## Folder Layout — Where Is The Code?

### SimHost

| Path | Relevance |
|---|---|
| `Hrot.SimHost/SimHostApp.cs` | Duplicate system registration (BUG2-N001), sender tracking (BUG2-N002), road network loading (BUG2-R001) |
| `Hrot.SimHost/SimHostVisualization.cs` | Inspector context menu Delete action (BUG2-E001) |
| `Hrot.SimHost/Modules/SimulationLogicModule.cs` | Road network blob property fix (BUG2-R001) |
| `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` | Missing trigger cases (BUG2-M001) |

### IG — Image Generator

| Path | Relevance |
|---|---|
| `Hrot.IG/IgApplication.cs` | Sender tracking (BUG2-N002), SHIFT drag mode (BUG2-I001), entity render layer catch-all (BUG2-V001), inspector Delete (BUG2-E001), IOS action routing (BUG2-E002) |
| `Hrot.IG/Tools/MeasureTool.cs` | Crosshair cursor (BUG2-T001) |
| `Hrot.IG/Systems/SelectionRenderSystem.cs` | Layer visibility for selection rings (BUG2-V001) |
| `Hrot.IG/Translators/ContextActionsUpdateTranslator.cs` | Map IOS action ID 10 → `IG_DeleteEntity` (BUG2-E002) |

### IOS — Operator Interface

| Path | Relevance |
|---|---|
| `Hrot.ExCon/Panels/MissionPanel.cs` | Trigger UI (BUG2-M002), button symbols (BUG2-M003), conflict UI (BUG2-M004) |
| `Hrot.ExCon/Panels/ConfigPanel.cs` | Remove legacy tool combo (BUG2-U001) |
| `Hrot.ExCon/Panels/OrbatPanel.cs` | Tree indentation fix (BUG2-U002) |
| `Hrot.ClusterRunner/Services/IosSubsystem.cs` | Sender tracking (BUG2-N002) |

### Map Common & Replication

| Path | Relevance |
|---|---|
| `Hrot.Map.Common/Replication/Egress/WorldPosEgressTranslator.cs` | WorldPos disposal (BUG2-N003) |
| `Hrot.Map.Common/Translators/EntityMissionIngressTranslator.cs` | Missing trigger cases (BUG2-M001) |

### FDP Toolkit — Vis2D

| Path | Relevance |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/BoxSelectionTool.cs` | Layer visibility in box selection (BUG2-V001) |
| `FDP/Toolkits/FDP.Toolkit.Vis2D/Layers/EntityRenderLayer.cs` | Catch-all layer mode (BUG2-V001) |
| `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/EntityPickerTool.cs` | Picker crosshair cursor (BUG2-T002) |

### FDP Toolkit — Combat / Behavior (Architecture task)

| Path | Relevance |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/` | Target location for unified `Health` component (BUG2-A001) |
| `FDP/Toolkits/FDP.Toolkit.Combat/Components/Health.cs` | Move to Contracts (BUG2-A001) |
| `Fdp.Kernel/Components/HealthData.cs` | Delete (BUG2-A001) |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` | Remove HealthData mirror sync (BUG2-A001) |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` | Read `Health` directly (BUG2-A001) |

### Network Demo (sender tracking only)

| Path | Relevance |
|---|---|
| `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs` | Sender tracking (BUG2-N002) |

---

## How to Build

```powershell
# Restore NuGet packages (once)
dotnet restore IOS-IG-SimHost.sln

# Build everything
dotnet build IOS-IG-SimHost.sln

# Run all tests
dotnet test IOS-IG-SimHost.sln
```

Individual project tests:
```powershell
dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj
dotnet test Hrot.ExCon.Tests/Hrot.ExCon.Tests.csproj
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj
dotnet test Hrot.Map.Common.Tests/Hrot.Map.Common.Tests.csproj
```

---

## Running the System

Start each application from the **workspace root** (`d:\Work\IOS-IG-SimHost-FDP-2`):

```bat
run_all_standalone.bat     # Launches SimHost + IG + IOS together
run_SimHost.bat            # SimHost standalone
run_IG.bat                 # IG standalone
run_IOS.bat                # IOS standalone
```

Or use the Runner to orchestrate all three:

```bat
run_all_together.bat
```
