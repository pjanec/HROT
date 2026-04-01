# Onboarding — Time Controller Unification

Welcome. This document orients you to the **time-ctrl-unif** workstream before you start coding.

---

## What We Are Refactoring

`FDP.Toolkit.Time` is the distributed time synchronisation toolkit used across the simulation
cluster (Orchestrator, SimHost, IG, CGF). It currently manages two time modes — **Continuous**
(real-time with PLL synchronisation) and **Deterministic** (lockstep frame stepping) — by
hot-swapping controller class instances at runtime alongside two separate coordination classes.

This workstream replaces that hot-swap design with:

| Component added | Replaces |
|---|---|
| `MasterSyncController` | `MasterTimeController` + `SteppedMasterController` + `DistributedTimeCoordinator` |
| `SlaveSyncController` | `SlaveTimeController` + `SteppedSlaveController` + `SlaveTimeModeListener` |
| `MasterLockstepTranslator` | half of `FrameLockstepDescriptorTranslator` (master side) |
| `SlaveLockstepTranslator` | half of `FrameLockstepDescriptorTranslator` (slave side) |

And introduces two new CQRS local message types: `AdvanceFrameIntent` and
`FrameStepCompletedEvent`.

**Role topology:** The Orchestrator is the **only** time master and the **only** source of
`TimePulseDescriptor`. SimHost, IG, and CGF are pure time slaves — they never emit time pulses.

Read [docs/DESIGN.md](./docs/DESIGN.md) for the full architectural rationale including the
problems being solved, the CQRS message split, and sequence diagrams of pause/step/resume.

---

## Planning Documents

| Document | Purpose |
|---|---|
| [docs/DESIGN.md](./docs/DESIGN.md) | Architecture, decisions, role topology, data-flow diagrams |
| [docs/TASK-DETAIL.md](./docs/TASK-DETAIL.md) | Detailed per-task specs with success conditions |
| [docs/TASK-TRACKER.md](./docs/TASK-TRACKER.md) | Progress checklist — update as tasks complete |

---

## Folder Layout

### Code being created / refactored

```
FDP/Toolkits/FDP.Toolkit.Time/
  Controllers/
    MasterSyncController.cs          ← NEW (TCU-MC001)
    SlaveSyncController.cs           ← NEW (TCU-SC001)
    MasterTimeController.cs          ← deleted in TCU-W006
    SteppedMasterController.cs       ← deleted in TCU-W006
    SlaveTimeController.cs           ← deleted in TCU-W006
    SteppedSlaveController.cs        ← deleted in TCU-W006
    SwitchableTimeController.cs      ← deleted in TCU-W006
    DistributedTimeCoordinator.cs    ← deleted in TCU-W006
    SlaveTimeModeListener.cs         ← deleted in TCU-W006
    SteppingTimeController.cs        ← KEPT (standalone tool use)
    TimeControllerFactory.cs         ← updated in TCU-W005
    TimeConfig.cs                    ← unchanged
    TimeControllerConfig.cs          ← unchanged
  Domain/
    TimeLocalEvents.cs               ← NEW (TCU-M002)
  Messages/
    TimeMessages.cs                  ← updated in TCU-M001
  Translators/
    MasterLockstepTranslator.cs      ← NEW (TCU-TR001)
    SlaveLockstepTranslator.cs       ← NEW (TCU-TR002)
    TimePulseIngressTranslator.cs    ← unchanged
    TimePulseEgressTranslator.cs     ← unchanged
  FrameLockstepDescriptorTranslator.cs  ← deleted in TCU-W006
  SwitchTimeModeDescriptorTranslator.cs ← unchanged
  TimeNetworkModule.cs               ← updated in TCU-TR003
```

### Application hosts being updated

```
Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs  ← TCU-W001
Hrot.SimHost/SimHostApp.cs                            ← TCU-W002
Hrot.CGF/CgfApplication.cs                           ← TCU-W003
Hrot.IG/IgApplication.cs                             ← TCU-W004
```

### Test projects

```
FDP/Toolkits/FDP.Toolkit.Time.Tests/                  ← new test files added in Phase 6
Hrot.ClusterRunner.Integration.Tests/                  ← existing integration tests; must remain green
Hrot.SimHost.Integration.Tests/                        ← existing integration tests; must remain green
```

---

## Build & Test

```powershell
# Build the FDP toolkit (from workspace root)
dotnet build FDP/FDP.sln

# Build the full solution (Hrot apps)
dotnet build IOS-IG-SimHost.sln

# Run time toolkit unit tests only
dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj

# Run all tests
dotnet test IOS-IG-SimHost.sln
```

---

## Developer Workflow

This project uses a **batch-based development workflow**. Before writing any code, read:

```
d:\Work\IOS-IG-SimHost-FDP\.dev\.guides\DEV-GUIDE.md
```

That document explains how tasks are batched into instructions, how to write batch reports, and
what quality standards apply to code and tests in this project.

In brief:
- Work one batch at a time; do not start the next batch until the current one is reviewed.
- Every task must have passing unit tests before the batch is considered complete.
- Update [docs/TASK-TRACKER.md](./docs/TASK-TRACKER.md) as tasks are completed.
