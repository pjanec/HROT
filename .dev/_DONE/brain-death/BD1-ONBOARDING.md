# BD1 — Onboarding Guide

Welcome to the **Brain-Death Lifecycle Fixes** workstream (prefix `BD1`).  
This document orients new developers on what we are fixing, where the code lives, and how to get started.

---

## What Are We Fixing?

After the CQRS **Brain/Muscle split** (NavigationIntent vs. NavState), several inter-related bugs emerged. The common root cause is that the ECS lifecycle for behavior and channel cleanup is **incomplete**: entities never cleanly reach a "brain death" state (no active behavior, no stimulated channels) when a mission finishes or is aborted. This causes:

| Symptom | Root cause |
|---|---|
| Vehicle overshoots destination and loops | `MissionDirectorSystem` never clears behavior at end of mission; `ChannelArbitrationSystem` never fires `OnExit` |
| Abort (CMD_ABORT_ALL) doesn't stop vehicles | `MissionControlRequestSystem` clears the queue but not `BehaviorState` |
| Shift+right-click waypoints disappear in 1 frame | Brain is still active; `NavigationIntentBridgeSystem` overwrites `NavState` on the next tick |
| Vehicles don't avoid each other (RVO broken) | `WithPhysics` and `SpawnEntityLocal` don't add `PhysicsCollider`; `SpatialHashSystem` ignores them |
| "Center on entity" teleports to top-left | `MapCanvas.Camera.Offset` defaults to `Vector2.Zero` in SimHost |

See [BD1-DESIGN.md](./BD1-DESIGN.md) for the full architectural analysis.

---

## Design & Task Documents

| Document | Purpose |
|---|---|
| [BD1-DESIGN.md](./BD1-DESIGN.md) | Architecture, root cause analysis, fix descriptions, end-to-end lifecycle walkthrough |
| [BD1-TASK-DETAIL.md](./BD1-TASK-DETAIL.md) | Per-task implementation specs with success conditions (unit tests) |
| [BD1-TASK-TRACKER.md](./BD1-TASK-TRACKER.md) | Progress checklist — update when tasks are completed |

All three files live in `docs/brain-death/`.

---

## Key Code Locations

### Files You Will Modify

| File | Task |
|---|---|
| *(new)* `FDP/Toolkits/FDP.Toolkit.Behavior/Events/BehaviorFinishedEvent.cs` | BD1-P1T0a |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/LocomotionDispatcherSystem.cs` | BD1-P1T0a |
| *(new)* `FDP/Toolkits/FDP.Toolkit.Behavior/Events/ClearBehaviorEvent.cs` | BD1-P1T0b |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs` | BD1-P1T0b |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` | BD1-P1T1 |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` | BD1-P1T2 |
| `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` | BD1-P1T3 |
| `Hrot.SimHost/SimHostVisualization.cs` | BD1-P2T1, BD1-P4T1 |
| `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs` | BD1-P3T1 |
| `Hrot.SimHost/UI/SimHostScenarioManager.cs` | BD1-P3T2 |
| `Hrot.NED/GenericDescriptors.cs` | BD1-P5T1 |
| `Hrot.Map.Common/Replication/Egress/EntityMasterEgressTranslator.cs` | BD1-P5T1 |
| Ingress translators consuming `EntityMaster.DisType` | BD1-P5T1 |
| `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ComponentReflector.cs` | BD1-P6T1 |
| `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs` | BD1-P7T1 |

### Key Supporting Files (Read-Only Reference)

| File | Why |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs` | `BehaviorState` struct definition |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Components/ChannelComponents.cs` | `LocomotionChannel`, `WeaponChannel`, `InteractionChannel` structs |
| `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorIds.cs` | `BehaviorIds.None = 0` and behavior constants |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Events/AssignBehaviorEvent.cs` | Pattern to follow for both new events |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs` | Sets `channel.Status = NodeStatus.Success` — the source of `BehaviorFinishedEvent` |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Systems/NavigationIntentBridgeSystem.cs` | Bridge between brain intent and muscle NavState |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs` | Spatial hash / RVO broadphase |
| `Fdp.Kernel/DISEntityType.cs` (or equivalent) | Engine-side DIS type with `ulong Value` overlay — do NOT modify |

### Test Projects to Extend

| Test Project | Tasks Covered |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/` | BD1-P1T0b, BD1-P1T1, BD1-P1T2 |
| `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/` | BD1-P1T0a (LocomotionDispatcher) |
| `Hrot.SimHost.Tests/` | BD1-P1T3, BD1-P2T1, BD1-P4T1, BD1-P7T1 |
| `Hrot.Map.Common.Tests/` | BD1-P3T1, BD1-P5T1 (egress) |
| `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/` | BD1-P3T1 (SpatialHashSystem test) |
| `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/` (or nearest host) | BD1-P6T1 |

---

## How to Build

From the workspace root:

```powershell
# Build the whole solution
dotnet build IOS-IG-SimHost.sln

# Or build just the FDP toolkits
dotnet build FDP/

# Run all tests
dotnet test IOS-IG-SimHost.sln --logger "console;verbosity=minimal"

# Run a specific test project
dotnet test FDP/Toolkits/FDP.Toolkit.Behavior.Tests/ --logger "console;verbosity=minimal"
dotnet test Hrot.SimHost.Tests/ --logger "console;verbosity=minimal"
```

To run SimHost standalone for manual verification:

```powershell
.\run_SimHost.bat
```

---

## Developer Process

Read `.dev-workstream/guides/DEV-GUIDE.md` before starting any work. It defines how to:
- Pick up a task batch
- Write code and tests
- Submit a batch report for review

The short version:
1. Work through tasks in phase order (Phase 1 → 2 → 3 → 4 → 5 → 6 → 7); the Phase 1 fixes are prerequisites for Phase 2 and 3 to work correctly. Within Phase 1: BD1-P1T0a and BD1-P1T0b are independent of each other but both must precede BD1-P1T2 and BD1-P1T3 respectively.
2. Every task must have passing unit tests matching the success conditions in [BD1-TASK-DETAIL.md](./BD1-TASK-DETAIL.md) before it can be marked done.
3. Update [BD1-TASK-TRACKER.md](./BD1-TASK-TRACKER.md) (change `[ ]` to `[x]`) when a task is complete.
