# Onboarding — CGF Scenario Fix 3

## Project Overview

This workstream fixes a multi-root bug where authoring and committing a mission in the HROT
Editor did not persist the mission to the saved scenario JSON. The investigation uncovered four
independent defects and one latent correctness issue in the distributed load path:

1. **Orphaned CGF systems** — `EditorSubsystem` calls `RegisterModule(cgfLogicPack)` which is a
   no-op; none of the CGF systems (including `MissionControlExecutionSystem`) ever tick in the
   editor.
2. **Wrong ECS API** — `MissionControlExecutionSystem` calls `repo.SetComponent` for
   `ActiveMissionPlan`, a managed class. The component lands in the wrong table and is invisible
   to `MissionPlanTranslator`.
3. **InlineArray defensive-copy trap** — `TryBuildQueue` writes to `queue.Phases[i]` on an `out`
   parameter, hitting a JIT-generated temporary. All phases are zeroed in the saved JSON.
4. **BrainBlackboard in scenario JSON** — The cognitive scratch-pad (128 opaque bytes) is
   serialized into the scenario file, violating the State vs. Message boundary.

Additionally:
5. **Phase misrouting in distributed CGF** — `MissionControlExecutionSystem` runs in the
   Simulation phase (background thread) instead of the Input phase.
6. **Child entity network ID corruption** — `StagingEntityExtractor` remaps root entity network
   IDs but not child entity network IDs during distributed load.

---

## Planning Artifacts

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Phased architectural design — WHAT and WHY |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specifications — HOW, scope, success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

---

## Folder Layout

### Files being modified

| File | Phase | Change |
|------|-------|--------|
| `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs` | 1 | SetManagedComponent fix + Span fix |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` | 1 | DataPolicy.NoSave on BrainBlackboard |
| `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SteppingTimeController.cs` | 1 | GetMode() fix |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/MissionControlModule.cs` | 2 | Two-group overload |
| `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` | 2 | Two-group overload |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | 2 | Input group wiring |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | 3 | Group wiring + MasterSyncController |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | 3 | Mirror EditorSubsystem fix |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | 4 | Child remapping fix |

### Files being created

| File | Phase | Purpose |
|------|-------|---------|
| `Hrot/Engine/Hrot.Common/Infrastructure/CgfInputGroupAdapter.cs` | 2 | Input-phase group adapter, shared between CgfSubsystem and EditorSubsystem |

### Related types (reference only — not modified)

| Type | Location | Role |
|------|----------|------|
| `ActiveMissionPlan` | `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/DomainMissionPlan.cs` | Managed ECS component holding the active mission |
| `MissionPlanQueue` | `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/MissionComponents.cs` | Unmanaged struct with [InlineArray] phases |
| `MasterSyncController` | `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` | Continuous/Deterministic time state machine |
| `TimeControllerFactory` | `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/TimeControllerFactory.cs` | Factory for all time controllers |
| `BrainBlackboard` | `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` | 128-byte cognitive scratch-pad |
| `StagingEntityExtractor` | `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | Two-pass scenario JSON → EntityCreationRequest extractor |
| `EditorPreviewController` | `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (nested) | ECS snapshot + time mode transitions for preview |

---

## Build and Run

**Build the solution:**
```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-restore
```

**Run all tests:**
```
dotnet test IOS-IG-SimHost.sln --no-build
```

**Run only the most relevant test projects for this workstream:**
```
dotnet test Hrot/Engine/Hrot.Common/Hrot.Common.Tests -- (if test project exists)
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build
```

---

## Development Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` to understand the batch-based development workflow
used in this repository. Tasks are grouped into batches; each batch has an INSTRUCTIONS file,
and the developer submits a REPORT that is reviewed before merging.

**Phase dependency order:** 1 → 2 → 3 → 4. Phases 2 and 4 are independent of each other but
both depend on Phase 1. Phase 3 depends on Phase 2 (because `EditorSubsystem` must call the new
two-group `CgfLogicPack.RegisterSystems` overload).
