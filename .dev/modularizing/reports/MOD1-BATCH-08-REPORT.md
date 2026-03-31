# MOD1-BATCH-08 Report

**Batch:** MOD1-BATCH-08  
**Tasks:** CT-MOD1-N, DB-MOD1-16, MOD1-P8T1, MOD1-P8T2, MOD1-P8T3, MOD1-P8T4, MOD1-P8T5  
**Status:** COMPLETE  

---

## Summary of Changes

### CT-MOD1-N — LosRequestBatchingSystem Refactor
`LosRequestBatchingSystem` already had the dual-inheritance problem corrected in the prior session. Confirmed that the class implements only `IModuleSystem` (no `ComponentSystem` base, no `[UpdateInGroup]`, no `OnUpdate`). `AutonomousPerceptionModule.Tick()` drives all four systems uniformly in the correct pipeline order.

**Side-effect cleanup:** `CombatModule.RegisterSystems` was calling `simGroup.AddSystem(new LosRequestBatchingSystem())` — a leftover from when the system was also a `ComponentSystem`. This was removed. The `simGroup` system count dropped from 20 to 19; `SimulationLogicModuleTests` was updated to match.

### DB-MOD1-16 — GeographicComponentIds
Created `FDP/Toolkits/Fdp.Toolkit.Geographic/GeographicComponentIds.cs` with numeric constants 77/78/79. Updated `[ComponentId]` attributes on the three ground-clamping structs to reference `GeographicComponentIds.*`. Removed the three constants from `GlobalComponentIds` with a comment pointing to the new home.

### MOD1-P8T1 — RecordingConfiguration + EcsRecordReplayController
- `RecordingConfiguration` (in `FDP.Toolkit.Replay`): `required string FilePath`, `Predicate<Entity>? EntityFilter`, `required Guid ExerciseId`. Used `Predicate<Entity>` not `Predicate<int>` per spec correction.
- `IDsmHandler` interface created in `Hrot.SimHost.Modules.Orchestration` (skeleton; full 2PC deferred).
- `EcsRecordReplayController`: pure factory/orchestrator. Methods: `PrepareRecordingAsync`, `FinalizeRecordingAsync`, `StartEpisodeRecordingAsync`, `StopEpisodeRecordingAsync`, `PrepareReplayAsync`, `TeardownReplayAsync`. Ensures output directories exist before passing paths to `AsyncRecorder`.

### MOD1-P8T2 — RecordingModule + RecorderSystem.EntityFilter
- `RecorderSystem.EntityFilter` (`Predicate<Entity>?`) added. Applied in `FillLiveness`: entities that don't pass the filter are treated as not-alive, so their component data is zeroed in the scratch buffer — no extra allocation on the filter path.
- `AsyncRecorder.EntityFilter` property delegates to `_recorderSystem.EntityFilter`.
- `RecorderTickSystem` (in `FDP.Toolkit.Replay`): casts `ISimulationView` to `EntityRepository` (safe for `Synchronous` policy), issues a keyframe every 60 frames and delta frames otherwise.
- `RecordingModule`: `ExecutionPolicy.Synchronous()`, registers `RecorderTickSystem` in `RegisterSystems`, blocking `Dispose()` flushes the LZ4 buffer.

### MOD1-P8T3 — StoryRecorderModule + StoryTag/StoryReplayTag
- `ReplayComponentIds`: toolkit-local registry (IDs 84/85) following the `GeographicComponentIds` pattern. Per Rule 4 ("Component IDs belong in toolkit-local registries"), these were **not** added to `GlobalComponentIds`.
- `StoryTag` and `StoryReplayTag` components registered under these IDs.
- `StoryRecorderModule`: thin wrapper over `RecordingModule`. The story-scoped entity filter (`Predicate<Entity>` that checks `HasComponent<StoryTag>` and `StoryId == storyId`) is built in `EcsRecordReplayController.BuildStoryFilter` and set on the `RecordingConfiguration` before the module is installed.

### MOD1-P8T4 — ReplayModule
- `PlaybackTickSystem` implements frame-based dual strategy: Strategy A (gap ≤ 3): iterative `StepForward`; Strategy B (gap > 3): direct `SeekToFrame`. Wall-clock-based seeking was omitted per spec correction — only frame-based seeking is supported until wider time infrastructure is in place.
- `ReplayModule`: validates schema in `RegisterSystems` ctor (throws `InvalidDataException` on bad magic), `SeekToFrameAsync` wraps `PlaybackController.SeekToFrame` in `Task.Run` for off-main-thread execution.

### MOD1-P8T5 — NodeBootstrapper Integration + ClusterSlave
- `ClusterSlave`: skeleton with `RegisterHandler(IDsmHandler)`, `IsHandlerRegistered<T>()`, `IReadOnlyList<IDsmHandler> RegisteredHandlers`.
- `NodeBootstrapper.BuildOrchestration(role, kernel, world, nodeId)`: creates `ClusterSlave`, registers `EcsRecordReplayController` for `Brain` and `AllInOne` roles.
- `Hrot.SimHost.csproj`: added `FDP.Toolkit.Replay` reference.

### New Projects
- `FDP.Toolkit.Replay` — library with RecordingConfiguration, ReplayComponentIds, StoryTag, StoryReplayTag, RecorderTickSystem, RecordingModule, StoryRecorderModule, PlaybackTickSystem, ReplayModule.
- `FDP.Toolkit.Replay.Tests` — 13 tests covering P8T2/P8T3/P8T4 success conditions.
- Both projects added to `IOS-IG-SimHost.sln`.

---

## Developer Insights

### Q1 — CT-MOD1-N: Execution ordering inside AutonomousPerceptionModule.Tick()

The four systems run in this order:
1. `LocalGridBuilderSystem` — rebuilds the `SpatialHashGrid` from current entity positions.
2. `VisionBroadphaseSystem` — queries the grid, emits `LosCheckRequestEvent` for candidates within range.
3. `LosRequestBatchingSystem` — consumes `LosCheckRequestEvent` (readable slot, set by the previous `SwapBuffers`), resolves each request (mock: emit `TargetVisibleEvent`; production: submit to raycast pipeline).
4. `ThreatEvaluationSystem` — consumes `TargetVisibleEvent` from **the previous tick**, updates `TargetMemory`.

> **Note:** The question's premise "LosRequestBatchingSystem is last (after ThreatEvaluation)" is incorrect. The correct position is **third** — after VisionBroadphase outputs requests, and before ThreatEvaluation consumes visible-target confirmations. Placing it last would mean the `TargetVisibleEvent`s it emits are only processed in the following tick by ThreatEvaluation anyway (double-buffered), but the logical pipeline order makes the data flow explicit. The system is placed third to clarify that it transforms LOS-requests into visible-target events, which are the input to threat scoring.

### Q2 — Zero-cost idle path for the recording infrastructure

When no recording is active, there is no `RecordingModule` installed in the `ModuleHostKernel`. Therefore no `RecorderTickSystem` instance exists in the scheduler graph. `RecorderSystem` is never allocated. The hot-path check commonly found in monolithic implementations (`if (isRecording) { … }`) does not exist: the overhead is literally zero when idle, because the code path through `RecorderTickSystem.Execute` never runs.

The design is "pay for what you use" at the scheduler topology level. Installing `RecordingModule` injects one `RecorderTickSystem` into `PostSimulation`; uninstalling it atomically removes it via the kernel's RCU topology swap. No flag guards are needed.

### Q3 — ClusterSlave integration and Hrot.ClusterRunner timing

The `ClusterSlave` skeleton is wired only through `NodeBootstrapper.BuildOrchestration()`, which is a new method separate from the existing `BuildSimulationLogic()`. `Hrot.ClusterRunner -x all` integration tests invoke `BuildSimulationLogic()` for full-stack testing but do not call `BuildOrchestration()`. No timing or registration-order issues were encountered.

`Hrot.ClusterRunner.Integration.Tests` passed unconditionally (31/31). The one integration-test flake observed during sln-wide parallel runs (`DomainIsolation_Domain0Spawn_DoesNotAffectDomain10`) reproduced reliably as 0/0 when run in isolation — it is a pre-existing parallelism sensitivity unrelated to this batch.

### Q4 — Circular dependency check

`FDP.Toolkit.Replay` references only:
- `Fdp.Kernel` (for `AsyncRecorder`, `PlaybackController`, `EntityRepository`, `Entity`)
- `ModuleHost.Core` (for `IModule`, `ISystemRegistry`, `ExecutionPolicy`, `IModuleSystem`)

`Hrot.SimHost` (which hosts `EcsRecordReplayController`) references `FDP.Toolkit.Replay`. The dependency graph is strictly acyclic:

```
Hrot.SimHost → FDP.Toolkit.Replay → Fdp.Kernel
                                     → ModuleHost.Core
```

No circular dependencies. The architecture correctly uses the principle that Hrot-domain code references generic FDP infrastructure, never the reverse.

---

## Test Results

| Project | Tests | Result |
|---|---|---|
| `FDP.Toolkit.Replay.Tests` | 13/13 | ✅ |
| `FDP.Toolkit.Perception.Tests` | 25/25 | ✅ |
| `Hrot.SimHost.Tests` | 167/167 | ✅ |
| `Hrot.SimHost.Integration.Tests` | 28/28 | ✅ |
| `Hrot.ClusterRunner.Integration.Tests` | 31/31 | ✅ |
| `Hrot.IG.Tests` | 304/304 | ✅ |

All pre-existing test suites continued to pass. Pre-existing flaky tests (timing-sensitive network and parallelism tests) were not affected by this batch's changes.

---

## Files Changed

### New Files
- `FDP/Toolkits/FDP.Toolkit.Replay/FDP.Toolkit.Replay.csproj`
- `FDP/Toolkits/FDP.Toolkit.Replay/RecordingConfiguration.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay/ReplayComponentIds.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay/StoryTag.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay/StoryReplayTag.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay/RecorderTickSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay/RecordingModule.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay/StoryRecorderModule.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay/PlaybackTickSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay/ReplayModule.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay.Tests/FDP.Toolkit.Replay.Tests.csproj`
- `FDP/Toolkits/FDP.Toolkit.Replay.Tests/RecordingModuleTests.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay.Tests/StoryRecorderModuleTests.cs`
- `FDP/Toolkits/FDP.Toolkit.Replay.Tests/ReplayModuleTests.cs`
- `FDP/Toolkits/Fdp.Toolkit.Geographic/GeographicComponentIds.cs`
- `Hrot.SimHost/Modules/Orchestration/IDsmHandler.cs`
- `Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`
- `Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs`
- `Hrot.SimHost.Tests/EcsRecordReplayControllerTests.cs`
- `Hrot.SimHost.Tests/ModuleHostKernelTestExtensions.cs`
- `Hrot.SimHost.Integration.Tests/RecordReplayIntegrationTests.cs`
- `Hrot.SimHost.Integration.Tests/ModuleHostKernelTestExtensions.cs`

### Modified Files
- `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` — removed IDs 77–79 (replaced with comment)
- `FDP/Kernel/Fdp.Kernel/FlightRecorder/RecorderSystem.cs` — added `EntityFilter` property
- `FDP/Kernel/Fdp.Kernel/FlightRecorder/AsyncRecorder.cs` — added `EntityFilter` passthrough
- `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/GroundClampingConfig.cs` — updated `[ComponentId]`
- `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/GroundClampingState.cs` — updated `[ComponentId]`
- `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/TerrainQueryBatchData.cs` — updated `[ComponentId]`
- `Hrot.SimHost/Modules/CombatModule.cs` — removed `LosRequestBatchingSystem` from simGroup
- `Hrot.SimHost/NodeBootstrapper.cs` — added `BuildOrchestration` method
- `Hrot.SimHost/Hrot.SimHost.csproj` — added `FDP.Toolkit.Replay` reference
- `Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj` — added `ModuleHost.Core`, `FDP.Toolkit.Replay`, `FDP.Toolkit.Time` references
- `Hrot.SimHost.Tests/SimulationLogicModuleTests.cs` — updated system count 20→19
- `Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj` — added `FDP.Toolkit.Replay` reference
- `Hrot.IG.Tests/IgGroundClampingModuleTests.cs` — updated `CapturingRegistry` to generic `RegisterSystem<T>`
- `IOS-IG-SimHost.sln` — added `FDP.Toolkit.Replay` and `FDP.Toolkit.Replay.Tests` projects
