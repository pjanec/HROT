# Onboarding — Integration Troubleshooting & Architecture Hardening

**Workstream:** Integration Bug Fixes + Architecture Consolidation  
**Date:** 2026-02-27

---

## 1. What We Are Building / Fixing

This workstream fixes the **five root causes** that prevent the Bagira distributed simulation stack from working end-to-end, and then consolidates the initialisation architecture to prevent the same class of issues from recurring.

The stack has three applications that collaborate over DDS:

| App | Role |
|-----|------|
| **SimHost** (`Bagira.SimHost`) | Authoritative vehicle/unit simulator; owns entities; publishes them via DDS |
| **IG** (`Bagira.IG`) | 2D tactical map view; receives ghost entities from SimHost; owned map drawings; driven by IOS via ImGui panels |
| **IOS** (`Bagira.IOS`) | Controller app; sends configuration, spawn requests, and interaction commands to IG and SimHost via DDS |
| **Runner** (`Bagira.Runner`) | Host process that can run all three in-process (`-m all`) for debugging and headless automated testing |

After the Phase 1 fixes, pressing "Spawn" or "New unit…" will produce real DDS traffic, maps will appear, entities will flow from SimHost into IG, and the Runner will correctly arbitrate input routing. Phase 2 eliminates the duplicate initialisation boilerplate. Phase 3 adds trace logging and an integration test.

---

## 2. Planning Artifacts

| Document | Location |
|---|---|
| Design | [docs/design/DESIGN-Integration-Troubleshooting.md](docs/design/DESIGN-Integration-Troubleshooting.md) |
| Task Details | [docs/design/TASK-DETAILS-Integration-Troubleshooting.md](docs/design/TASK-DETAILS-Integration-Troubleshooting.md) |
| Task Tracker | [docs/design/TASK-TRACKER-Integration-Troubleshooting.md](docs/design/TASK-TRACKER-Integration-Troubleshooting.md) |
| Source Design Talk | [FDP/Docs/troubleshooting-integration/Troubleshooting_FDP_Integration.json.md](FDP/Docs/troubleshooting-integration/Troubleshooting_FDP_Integration.json.md) |

Start with the Design document, then use the Task Tracker to navigate to specific Task Details.

---

## 3. Folder Layout

### Components Being Fixed / Refactored

| Folder | Contents |
|---|---|
| `Bagira.SimHost/` | `SimHostApp.cs` (initialisation), `UI/SimHostScenarioManager.cs` (spawn fix — RC-2) |
| `Bagira.IG/` | `IgApplication.cs` (TKB fix RC-1, translator wiring RC-5), `IosMock.cs` is actually in IOS |
| `Bagira.IOS/` | `IosMock.cs` (DockSpace fix RC-4), `Program.cs` (NullDdsWriter fix RC-3) |
| `Bagira.Runner/Services/` | `IosSubsystem.cs` (NullDdsWriter fix RC-3), `SubsystemOrchestrator.cs` (headless fix Phase 2) |
| `Bagira.Map.Common/` | New file `BagiraEnvironment.cs` (Phase 2 bootstrapper) |

### Existing Infrastructure Referenced By This Workstream

| Folder | Contents |
|---|---|
| `FDP/` | FDP ECS engine, toolkits, and module host — do not modify |
| `FDP/Toolkits/FDP.Toolkit.NetworkSpawning/` | `NetworkSpawningSystem` — reads TKB; RC-1 and RC-2 surface here |
| `FDP/Toolkits/FDP.Toolkit.Lifecycle/` | `EntityLifecycleModule` — ELM; transition to Active required before DDS egress |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Translators/` | `EntityMasterTranslator`, `CycloneEgressSystem` — DDS publish/receive boundary |
| `Bagira.Map.Common/Commands/` | `BdcCommandGateway` — already exists; wire into IG (RC-5) |
| `Bagira.Map.Definitions/Tkb/` | `BdcTkbCatalog` — must be registered (RC-1) |
| `Bagira.DDS.DataModel/` | All DDS topic types (`MapInteractionConfig`, `CreateEntityRequest`, etc.) |

### Test Projects

| Folder | Purpose |
|---|---|
| `Bagira.IG.Tests/` | Unit tests for IG subsystem |
| `Bagira.SimHost.Tests/` | Unit tests for SimHost subsystem |
| `Bagira.IOS.Tests/` | Unit tests for IOS logic |
| `Bagira.SimHost.Integration.Tests/` | Integration tests — add INTS-P3-014 here |

---

## 4. Build & Run

### Build the Solution

```powershell
dotnet build IOS-IG-SimHost.sln
```

### Run All Tests

```powershell
dotnet test IOS-IG-SimHost.sln
```

### Run the Full Stack (Runner)

```powershell
cd Bagira.Runner.Standalone
dotnet run -- -m all
```

### Run Individual Apps

```powershell
# SimHost only
cd Bagira.SimHost.Standalone
dotnet run

# IG only
cd Bagira.IG.Standalone
dotnet run

# IOS only
cd Bagira.IOS.Standalone
dotnet run
```

### Run Integration Tests Only

```powershell
dotnet test Bagira.SimHost.Integration.Tests/
```

---

## 5. Development Workflow

Read **`.dev-workstream/guides/DEV-GUIDE.md`** before starting any task. It defines the batch-based development workflow used on this project, including how to receive batch instructions, write batch reports, and handle review feedback.

The task tracker for this workstream is at [docs/design/TASK-TRACKER-Integration-Troubleshooting.md](docs/design/TASK-TRACKER-Integration-Troubleshooting.md). Update it as tasks complete.
