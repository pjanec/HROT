# Onboarding: CGF Scenario Serialization Correctness (cgf-scn-2)

## Project Overview

This workstream fixes scenario serialization correctness in the distributed Hrot CGF cluster.
The immediate symptom was a `FireAtTarget` mission assigned in `Hrot.Editor` disappearing from
the saved scenario JSON.  Investigation revealed several related gaps:

- Mission data (`ActiveMissionPlan`, `MissionPlanQueue`) is silently dropped by the
  auto-serializer.
- Execution-tier components (`WeaponChannel`, `BrainBTreeState`, etc.) pollute the scenario
  DOM with volatile mid-tick state that should never persist.
- Cross-entity references in structural components (`PassengerBuffer`, `VisHierarchyNode`,
  etc.) become dangling pointers during distributed genesis.
- In-flight FDP events are not preserved in checkpoint binary files.
- The `FdpAutoSerializer` expression-tree compiler truncates `fixed` buffers and `[InlineArray]`
  to their first element.

All fixes are additive: the `ReferencePreviewHandler`-based distributed preview already works
correctly and requires no changes.

## Planning Artifacts

| File | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Phased architectural design — WHY and WHAT |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specs with success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

## Folder Layout

### Components being modified

| Path | What |
|---|---|
| `FDP/Engine/Fdp.Core/DataPolicyAttribute.cs` | DataPolicy enum — fix misleading comments |
| `FDP/Engine/Fdp.Core/FdpEventBus.cs` | Add `PopulateCurrentStreams` methods |
| `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs` | Add `serializeReadBuffer` parameter to `WriteEvents` |
| `FDP/Engine/Fdp.Core/Orchestration/CheckpointIOWorker.cs` | Pass event bus to `RecordKeyframe` |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs` | Add `[DataPolicy(DataPolicy.NoSave)]` to channels |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs` | Add `[DataPolicy(DataPolicy.NoSave)]` to brain execution |
| `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` | Add `[DataPolicy(DataPolicy.NoSave)]` to perception runtime |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` | Fixed buffer + InlineArray expression tree support |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceCheckpointHandler.cs` | Wire `EventAccumulator` |

### Components being created

| Path | What |
|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Serializers/MissionPlanTranslator.cs` | Scenario translator for `ActiveMissionPlan` + `MissionPlanQueue` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/IsEmbarkedTagTranslator.cs` | Translator emitting `InitialVehicleIntent` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/VisHierarchyNodeTranslator.cs` | Translator emitting `InitialHierarchyIntent` |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PersonalRouteRefTranslator.cs` | Translator emitting `InitialRouteIntent` |
| `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs` | Intent DTO components for distributed genesis |
| `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs` | Late-binding system for Intent components |

### Components being deleted

| Path | What |
|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Serializers/WeaponChannelTranslator.cs` | Superseded by `DataPolicy.NoSave` on `WeaponChannel` |

### Key registration sites

| Path | What needs changing |
|---|---|
| `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | Register `MissionPlanTranslator` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Remove `WeaponChannelTranslator`; add `MissionPlanTranslator` |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Add `MissionPlanTranslator` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Wire `EventAccumulator` into `ReferenceCheckpointHandler` |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | Patch Intent NetworkId remapping |

## Build and Run Tests

```powershell
# Build entire solution
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-restore

# Run relevant test projects
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --no-build
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build
```

## Developer Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` for the batch-based development workflow
used in this repository.  All implementation is done in batches; each batch has an
INSTRUCTIONS file and a REPORT file under `.dev/cgf-scn-2/batches/` once created.

## Key Concepts for New Developers

**Two persistence paths, two guards:**
- `DataPolicy.NoSave` excludes a component from **scenario JSON** (processed by `ScenarioSerializer` / `FdpAutoSerializer`).
- `DataPolicy.NoRecord` excludes a component from **binary checkpoints** (processed by `RecorderSystem.RecordKeyframe`).
- A component can carry both flags (`DataPolicy.Transient` = all three exclusions).

**Scenario vs. Checkpoint:**
- **Scenario** — declarative authoring template; initial conditions only; must be portable
  across nodes.
- **Checkpoint** — exact binary memory clone via `EntityRepository.SyncFrom()`; preserves
  every transient pointer, execution frame, and active buffer identically.

**IEntityScenarioTranslator pattern:**
- `GetConsumedComponentsMask()` — bits cleared from the auto-serializer fallback.
- `Extract` — entity state → DOM dictionary (convert `Entity` handles to GUIDs via `IGuidResolver`).
- `Inject` — DOM dictionary → entity state (resolve GUIDs back to `Entity` handles).

**Distributed genesis safety:**
- Entity handles are valid only within the `EntityRepository` that issued them.
- During distributed scenario load, referenced entities may not have spawned yet on the
  receiving node.
- Intent components (`InitialPassengersIntent`, etc.) defer resolution to
  `GenesisMaterializationSystem` which waits until all referenced Network IDs appear in
  `NetworkEntityMap`.
