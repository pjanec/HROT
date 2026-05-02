# BS-1 Onboarding Guide — Brain / Muscle Node Separation

Welcome to the **BS-1** workstream. This document gives you everything you need to get
up to speed and start contributing.

---

## 1. What Are We Building?

The simulation engine supports a distributed multi-node topology where **Brain** (AI cognition)
and **Muscle** (physics / kinematics) roles run as separate processes that communicate over DDS.

This workstream **enforces that separation end-to-end**. Several subsystems currently violate the
architectural boundary — most critically:

- The `CombatModule` (ballistics, hit resolution, damage) runs on the Brain node when it should
  only run on the Muscle node.
- `DamageSystem` applies damage on every node without an `HasAuthority` guard.
- The `EntityDamageEgressTranslator` is missing — health state is never published to DDS.
- Navigation executors (`FleeExecutor`, `FollowRoadGraphExecutor`, `FollowRouteExecutor`,
  `Action_Wander`) directly write the Muscle-owned `NavState` component instead of going
  through the `NavigationIntent` CQRS channel.
- `MissionDirectorSystem` polls `NavState.HasArrived` (a Muscle-tier field) to detect arrival,
  which silently fails in a distributed topology.

After this workstream the **Brain node is a pure cognitive tier**: it writes intents, consumes
sensor data, and evaluates behaviors — it never touches physics components directly.

---

## 2. Planning Artifacts

| Document | Purpose |
|---|---|
| [`BS-1-DESIGN.md`](./BS-1-DESIGN.md) | Architecture overview, phases, decisions |
| [`BS-1-TASK-DETAIL.md`](./BS-1-TASK-DETAIL.md) | Detailed spec for every task (scope, constraints, success conditions) |
| [`BS-1-TASK-TRACKER.md`](./BS-1-TASK-TRACKER.md) | Quick checklist — mark tasks done here |

Read **DESIGN.md first**, then open the specific task detail before starting any task.

---

## 3. Folder Layout

### Components being refactored

| Path | Why it matters |
|---|---|
| `Hrot.SimHost/NodeBootstrapper.cs` | Module-to-role assignments — changes in Phase 4 |
| `Hrot.SimHost/SimHostApp.cs` | Egress translator registration — changes in Phase 4 |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` | Add `HasAuthority` guard (Phase 1) |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs` | Consumes new events (Phase 2) |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HitResolutionSystem.cs` | Emits `DetonationNotification` (Phase 3) |
| `FDP/Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs` | Publishes `WeaponFireIntent` (Phase 2) |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FleeExecutor.cs` | Replace NavState → NavigationIntent (Phase 5) |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRoadGraphExecutor.cs` | Replace NavState → NavigationIntent (Phase 5) |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRouteExecutor.cs` | Replace NavState → NavigationIntent (Phase 5) |
| `Hrot.SimHost/Brains/SimHostNodes.cs` | Remove NavState poll in `Action_Wander` (Phase 5) |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` | Fix `ReachedDestination` trigger (Phase 5) |

### New files to create

| Path | Task |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Events/WeaponFireEvents.cs` | BS1-T001 |
| `FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs` | BS1-T002 |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageCalculationSystem.cs` | BS1-T012 |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs` | BS1-T014 |
| `FDP/Toolkits/FDP.Toolkit.Combat/Modules/DamageAssessmentModule.cs` | BS1-T012 |
| `Hrot.NED/FireInteractionMessages.cs` (extend) | BS1-T001, BS1-T002 |
| `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs` | BS1-T005 |
| `Hrot.SimHost/Network/Egress/WeaponFireNotificationEgressTranslator.cs` | BS1-T008 |
| `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs` | BS1-T011 |
| `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs` | BS1-T013 |
| `Hrot.Map.Common/Replication/Egress/EntityDamageEgressTranslator.cs` | BS1-T015 |
| `Hrot.SimHost/Network/Ingress/WeaponFireRequestIngressTranslator.cs` | BS1-T006 |
| `Hrot.SimHost/Network/Ingress/MunitionDetonationIngressTranslator.cs` | BS1-T012 |
| `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs` | BS1-T014 |
| `Hrot.IG/Translators/WeaponFireIngressTranslator.cs` | BS1-T009 |

### Key existing infrastructure (read before writing new translators)

| Path | What to learn |
|---|---|
| `Hrot.Map.Common/Replication/Egress/EntityInfoEgressTranslator.cs` | Pattern for component-change-tracking egress |
| `Hrot.Map.Common/Replication/Ingress/EntityDamageIngressTranslator.cs` | Pattern for ingress translator |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs` | Gold-standard CQRS executor (NavigationIntent → NavigationStatus) |
| `Hrot.Map.Common/Replication/FireInteractionEventTranslator.cs` | Event-egress pattern |

---

## 4. Build & Run

**Build the full solution:**

```
dotnet build IOS-IG-SimHost.sln
```

From the workspace root (`d:\Work\IOS-IG-SimHost-FDP-2`).

**Run tests:**

```
dotnet test IOS-IG-SimHost.sln
```

**Run all standalone (individual processes):**

```
build_all_standalone.bat
run_all_standalone.bat
```

**Run all-in-one (single process, easiest for development):**

```
run_all_together.bat
```

---

## 5. Development Workflow

This workstream uses a **batch-based workflow**. Before writing any code, read:

```
.dev-workstream/guides/DEV-GUIDE.md
```

That guide defines how batches are issued, how to write batch reports, how reviews work, and
what quality standards are expected for code and tests.

Key points:
- Implement tasks from the batch instruction file, not directly from the task tracker.
- Write unit tests whose scenarios match the **Success Conditions** in `BS-1-TASK-DETAIL.md`.
- Submit a batch report when your batch is complete.
- Do not start the next batch until the current batch is reviewed and approved.

---

## 6. Architecture Quick Reference

### NavigationIntent CQRS (the pattern to follow)

```
Brain executor                         Muscle
  write NavigationIntent ─────────────► NavigationIntentBridgeSystem
                                            └─► writes NavState
                                         CarKinematicsSystem
                                            └─► updates NavState.HasArrived
  read NavigationStatus  ◄────────────── NavigationExecutionSystem
                                            └─► publishes NavigationStatus
```

All Phase 5 executor refactors must follow this exact same pattern.

### Weapon Fire CQRS (what this workstream implements)

```
Brain executor                         Muscle
  publish WeaponFireIntent (ECS)
  WeaponFireIntentEgressTranslator ──► WeaponFireRequest (DDS)
                                       WeaponFireRequestIngressTranslator
                                         └─► publish WeaponFireIntent (ECS)
                                       FireProcessingSystem
                                         └─► spawn bullet
                                         └─► publish WeaponFireNotification (ECS)
                                       WeaponFireNotificationEgressTranslator ──► WeaponFire (DDS) → IG
```
