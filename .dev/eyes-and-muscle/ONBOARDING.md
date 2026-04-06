# Onboarding — EyesAndMuscle Workstream

Welcome to the EyesAndMuscle development workstream.

---

## 1. What is being built

This workstream delivers three things in sequence:

1. **DRY initialization infrastructure** (`HrotNodeBuilder` / `HrotNodeContext`) — shared boilerplate for bootstrapping any Hrot network node (ECS world, time controller, DDS participant, cluster slave) so future subsystems don't duplicate it.

2. **`NedReplicationModule`** — a single `IEcsModule` that bundles the NED CycloneDDS translators together with the ECS systems that depend on NED-specific components (dead-reckoning/smoothing, ghost lifecycle). This makes the network boundary explicit and swap-safe for future data models.

3. **`EyesAndMuscleSubsystem`** — a combined Muscle (physics simulation) + Eyes (2D presentation) `ISubsystem` that runs both concerns in one process. It uses the two new building blocks above and includes an `EyesAndMuscleModule` that demonstrates the Snapshot-on-Demand (SoD) asynchronous pattern the future Stride engine integration will rely on.

The Stride engine itself is **not** part of this workstream — it is postponed. EyesAndMuscle is the stepping stone that validates the architecture before Stride arrives.

---

## 2. Planning artifacts

| Document | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Phased architecture — WHAT and WHY |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specs — scope, constraints, success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

---

## 3. Folder layout

```
Hrot.ClusterRunner/
  Infrastructure/
    HrotNodeBuilder.cs         ← NEW  (EAM-I001)
    HrotNodeContext.cs         ← NEW  (EAM-I001)
  Replication/
    NedReplicationModule.cs    ← NEW  (EAM-N001)
  Services/
    SimHostSubsystem.cs        ← existing
    IgSubsystem.cs             ← existing
    EyesAndMuscleSubsystem.cs  ← NEW  (EAM-E001)
    EyesAndMuscleModule.cs     ← NEW  (EAM-E002)

Hrot.ClusterRunner.Integration.Tests/
  EyesAndMuscleIntegrationTests.cs  ← NEW (EAM-E003)

Hrot.SimHost/
  NodeBootstrapper.cs          ← existing; may be extended for shared orchestration logic
  Network/
    KinematicTranslatorPack.cs ← may need visibility change (EAM-N002)
    SharedTranslatorPack.cs    ← may need visibility change (EAM-N002)

Hrot.IG/
  Systems/
    DeadReckoningSyncSystem.cs ← existing; referenced from NedReplicationModule
  Translators/
    EntityStatesIngressPack.cs ← may need visibility change (EAM-N002)
```

**Key existing classes to understand before starting:**

| Class | Location | Why it matters |
|---|---|---|
| `SimHostApp.OnLoad` | `Hrot.SimHost/SimHostApp.cs` | The current monolithic init — what we're refactoring *from* |
| `NodeBootstrapper` | `Hrot.SimHost/NodeBootstrapper.cs` | Existing role-based composition root — reuse `BuildOrchestration` |
| `KinematicTranslatorPack` | `Hrot.SimHost/Network/` | NED kinematic translators (NavIntent ingress, WorldPos egress) |
| `SharedTranslatorPack` | `Hrot.SimHost/Network/` | Entity lifecycle translators (all roles) |
| `EntityStatesIngressPack` | `Hrot.IG/Translators/` | IG visual-state ingress translators |
| `DeadReckoningSyncSystem` | `Hrot.IG/Systems/` | NED-coupled DR/smoothing — goes into NedReplicationModule |
| `GhostCreationSystem` | `FDP.Toolkit.Replication.Systems` | Entity replica materialisation on ingress |
| `SimHostCoreLogicPack` | `Hrot.SimHost/SimHostCoreLogicPack.cs` | Composite module: kinematic + combat logic |
| `IgPresentationModule` | `Hrot.SimHost/Modules/IgPresentationModule.cs` | Visualization layer (MapCanvas renderer) |
| `HrotRunnerHarness` | `Integration.Tests/HrotRunnerHarness.cs` | Integration test harness used by EAM-E003 |
| `SubsystemOrchestrator` | `FDP/Framework/FDP.Framework.Runner/` | Orchestrates subsystem lifecycle |
| `ISubsystem` | `FDP/Framework/FDP.Framework.Runner/` | Interface all subsystems implement |
| `IEcsModule` | `ModuleHost.Core/` | Interface all FDP kernel modules implement |

---

## 4. Build and run

**Build the solution:**
```
dotnet build IOS-IG-SimHost.sln --no-restore
```

**Run unit tests for ClusterRunner:**
```
dotnet test Hrot.ClusterRunner.Tests --no-build
```

**Run integration tests (requires DDS daemon or headless mode):**
```
dotnet test Hrot.ClusterRunner.Integration.Tests --no-build
```

**Run only EyesAndMuscle integration tests (once created):**
```
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "EyesAndMuscleIntegrationTests" --no-build
```

**Run EyesAndMuscle standalone (once built):**
```
dotnet run --project Hrot.ClusterRunner -- --subsystems EyesAndMuscle,Orchestrator
```

---

## 5. Development workflow

This workstream follows the batch-based development workflow described in the project's developer guide.

Before starting any task, read [.dev-workstream/guides/DEV-GUIDE.md](./../guides/DEV-GUIDE.md) (if present) to understand the batch-based workflow, review/approval process, and how to write batch reports.

**Recommended task order:**
1. EAM-I001 → EAM-I002 (builders first, everything depends on them)
2. EAM-N002 → EAM-N001 (visibility changes before the module that uses them)
3. EAM-E001 → EAM-E002 → EAM-E003 (subsystem shell, then async module, then tests)

**Key "gotcha" to be aware of:**

`DeadReckoningSyncSystem` is in `Hrot.IG` and will be referenced from `NedReplicationModule` (in `Hrot.ClusterRunner`). This is legal because `Hrot.ClusterRunner` already references `Hrot.IG`. Do not create a project reference in the other direction.
