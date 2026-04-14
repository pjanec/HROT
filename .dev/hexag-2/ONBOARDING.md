# Onboarding: OrchestratorSubsystem Hexagonal Architecture & Bus Unification

Welcome to the **hexag-2** workstream.  This document gives a new developer everything needed
to understand the goal, find the relevant code, build the project, and start contributing.

---

## What Are We Building / Refactoring?

This workstream fixes two related problems in the `OrchestratorSubsystem`:

1. **IsPaused UI bug** — The cluster control panel pauses simulated time but keeps showing
   "RUNNING" instead of "[PAUSED]".  Root cause: the subsystem owns two separate `FdpEventBus`
   instances; `MasterSyncController` publishes `SwitchTimeModeEvent` to one while
   `ClusterUiCache` listens on the other.

2. **Hexagonal Architecture violations** — `OrchestratorSubsystem` directly calls
   `HrotEnvironment.CreateParticipant()`, instantiates concrete DDS translator classes, and
   manages a `DdsIdAllocatorServer` background thread — all of which must live in the
   infrastructure layer, not the domain.

The goal is to leave `OrchestratorSubsystem` with zero CycloneDDS imports: it should depend
only on `FdpEventBus`, `INetworkFactory`, and domain-level interfaces.

---

## Where to Read

| Document | Purpose |
|----------|---------|
| [design-talk.md](./design-talk.md) | Original design conversation; start here for full context |
| [DESIGN.md](./DESIGN.md) | Formal architecture and phased plan |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specs with success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Brief checklist of tasks and their status |
| [.dev/.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md) | Developer workflow and code standards |

---

## Relevant Components

### Domain (being refactored)

| Path | Description |
|------|-------------|
| `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` | The core subsystem — primary change target |
| `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterUiCache.cs` | CQRS read-model; reads `SwitchTimeModeEvent` from bus |
| `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterScenarioPanel.cs` | UI panel reading `IsPaused` from cache |
| `Hrot/Subsystems/Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs` | To be moved to `Hrot.Network.Orchestration` |
| `Hrot/Subsystems/Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs` | To be moved to `Hrot.Network.Orchestration` |

### Domain infrastructure (reference)

| Path | Description |
|------|-------------|
| `Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs` | Factory interface; will gain `CreateOrchestratorTranslators` |
| `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` | Publishes `SwitchTimeModeEvent` |

### Infrastructure (network layer)

| Path | Description |
|------|-------------|
| `Hrot/Network/Hrot.Network.Orchestration/` | Where `NodeOpSlaveTranslator` and `OrchestrationObserverTranslator` live; master translators land here |
| `Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs` | Concrete factory; will implement `CreateOrchestratorTranslators` |

### Composition root

| Path | Description |
|------|-------------|
| `Hrot/Runner/Hrot.ClusterRunner/` | Application entry point; wires `INetworkFactory` into subsystems |

### Tests

| Path | Description |
|------|-------------|
| `Hrot.ClusterRunner.Integration.Tests/` | End-to-end tests; must all pass after changes |
| `Hrot.Orchestrator.Tests/` (if exists) | Unit tests for orchestrator domain |

---

## How to Build

From the workspace root (`d:\WORK\IOS-IG-SimHost-FDP`):

```
dotnet build IOS-IG-SimHost.sln
```

Build a single project:

```
dotnet build Hrot\Subsystems\Hrot.Orchestrator\Hrot.Orchestrator.csproj
```

---

## How to Run the Tests

Run all integration tests (excluding DDS-dependent ones if DDS is not available):

```
dotnet test Hrot.ClusterRunner.Integration.Tests\Hrot.ClusterRunner.Integration.Tests.csproj --no-build -v q
```

Run a specific test by name:

```
dotnet test Hrot.ClusterRunner.Integration.Tests --no-build --filter "PauseStepResume_SimTimeAdvancesByStepAmount"
```

---

## Developer Workflow

Read [DEV-GUIDE.md](../.guides/DEV-GUIDE.md) before making any changes.  Key points:
- Work task-by-task as listed in [TASK-TRACKER.md](./TASK-TRACKER.md).
- Each task has explicit success conditions in [TASK-DETAIL.md](./TASK-DETAIL.md).
- Mark tasks done in `TASK-TRACKER.md` as you complete them.
- Every task must leave the solution building without errors before you move on.
