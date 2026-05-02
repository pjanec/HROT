# BATCH-09 Report

**Batch:** BATCH-09  
**Developer:** GitHub Copilot  
**Date:** 2026-03-26  
**Status:** Complete (Tasks 1–4 fully delivered; Task 5 Phase A delivered)

---

## Task Completion

| Task | Status | Notes |
|------|--------|-------|
| Task 1 — `LocalGridBuilderSystem` incremental updates | ✅ Complete | Per-entity Remove+Add; FreeList in SpatialHashGrid |
| Task 2 — `AutonomousPerceptionModule` scoped bus contract | ✅ Complete | XML doc whitelist + isolation regression test |
| Task 3 — `RecordingModule` blocking option | ✅ Complete | `RecordingConfiguration.Blocking` flag + wired through |
| Task 4 — Rename `DemoBehaviorIds.cs` | ✅ Complete | File renamed to `BehaviorValidationBehaviorIds.cs` |
| Task 5 — DEM1-D009 Phase A DistributedTank | ✅ Phase A complete | Two kernels + 2 DDS participants; see Phase B follow-ups |

---

## Testing Results

| Project | Before | After | Notes |
|---------|--------|-------|-------|
| `FDP.Toolkit.Perception.Tests` | 27/27 | **32/32** | +4 LocalGridBuilder tests + 1 scoped bus isolation test |
| `FDP.Toolkit.CarKinem.Tests` | 126/126 | **129/129** | +3 SpatialHashGrid Remove/FreeList tests |
| `FDP.Toolkit.Replay.Tests` | 13/13 | **14/14** | +1 blocking config test |
| `Fdp.Examples.Scenarios.Tests` | 51/51 | **53/53** | +2 DistributedTank Phase A tests |
| `FDP.Toolkit.Physics.Tests` | 25/25 | 25/25 | No change, no regressions |
| Solution build | Clean | **Clean** | Zero new errors or warnings |

---

## Implementation Details

### Task 1 — `LocalGridBuilderSystem` incremental updates

**Problem:** The dirty path cleared the entire grid (`Clear()` O(n_cells)) and re-inserted all entities on any movement — worst case O(n_entities) even when only one entity moved.

**Solution:**
- Added `NativeArray<int> FreeList` + `int FreeListCount` fields to `SpatialHashGrid`.
- Added `SpatialHashGrid.Remove(Entity entity, Vector2 previousPosition)` — linked-list splice in O(chain_length) ≈ O(1) for sparse cells + free-list push.
- Modified `SpatialHashGrid.Add` to pop free-list slots before allocating `EntityCount++`.
- Updated `LocalGridBuilderSystem.Execute` to use per-entity incremental updates:
  - Count changed → full `Clear()`+rebuild (count change means entity set is stale).
  - Per entity: if position unchanged → skip; if moved → `Remove(oldPos)` + `Add(newPos)`.

**Complexity after change:**
- Static frame: O(n) scan, 0 grid writes.
- k entities moved: O(k) Remove+Add pairs.
- All entities moved (worst case): O(n) — same as before.
- Count changed: O(n) full rebuild — same as before.

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Spatial/SpatialHashGrid.cs`
- `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs`

**Tests added:**
- `SpatialHashGridTests.SpatialHashGrid_Remove_SplicesEntryFromLinkedList`
- `SpatialHashGridTests.SpatialHashGrid_FreeList_ReusesSlotsAfterRemove`
- `SpatialHashGridTests.SpatialHashGrid_Remove_ReturnsFalse_WhenEntityNotPresent`
- `LocalGridBuilderSystemTests.LocalGridBuilder_FirstTick_FullRebuild_EntityIsQueryable`
- `LocalGridBuilderSystemTests.LocalGridBuilder_Incremental_MovedEntity_IsFoundAtNewCell`
- `LocalGridBuilderSystemTests.LocalGridBuilder_Incremental_StaticEntities_RemainQueryable`
- `LocalGridBuilderSystemTests.LocalGridBuilder_FullRebuild_OnEntityCountChange`

---

### Task 2 — `AutonomousPerceptionModule` scoped bus contract

**Problem:** `PerceptionScopedView.ConsumeEvents<T>` always reads `_scopedBus` but the whitelist of registered event types was implicit and undocumented, risking silent empty reads for future developers.

**Solution:**
- Extended `ConsumeEvents<T>` XML doc to explicitly list the two registered types (`LosCheckRequestEvent`, `TargetVisibleEvent`) and state why world-bus events are intentionally inaccessible.
- Added a note on the protocol future systems must follow to add new event types.

**Design note (for report):**
The current isolation contract is correct. The scoped bus is a private pipeline channel. Any event type a system needs to observe must be registered on `_scopedBus` and mirrored from the world bus by an upstream system, or read via a separate view path. The whitelist prevents silent empty reads without the O(n) overhead of dual-bus fan-out.

**Test added:**
- `AutonomousPerceptionModuleTests.AutonomousPerceptionModule_ScopedEvents_DoNotLeakToWorldBus` — triggers a full perception tick with an observer+target pair and asserts `LosCheckRequestEvent` / `TargetVisibleEvent` are absent from the world bus afterward.

---

### Task 3 — `RecordingModule` blocking option

**Problem:** `RecordingConfiguration` / `RecorderTickSystem` had no blocking mode; tight-loop scenarios had to bypass `RecordingModule` and use `AsyncRecorder.CaptureFrame(blocking: true)` directly.

**Solution:**
- Added `bool Blocking { get; init; } = false;` to `RecordingConfiguration` (production default non-blocking, backward compatible).
- Added `bool blocking = false` parameter to `RecorderTickSystem` constructor; stores as `_blocking`.
- Both `CaptureKeyframe` and `CaptureFrame` calls in `Execute` now forward `blocking: _blocking`.
- `RecordingModule.RegisterSystems` passes `_config.Blocking` when constructing `RecorderTickSystem`.

**Usage:**
```csharp
var config = new RecordingConfiguration
{
    FilePath  = "my.fdp",
    ExerciseId   = Guid.NewGuid(),
    Blocking  = true,   // prevents delta drops in tight synchronous loops
};
kernel.RegisterModule(new RecordingModule(config));
```

**Test added:**
- `RecordingModuleTests.RecordingModule_BlockingTrue_WritesFileSuccessfully` — drives 5 ticks in blocking mode, asserts file exists after Dispose.

---

### Task 4 — Rename `DemoBehaviorIds.cs`

- Renamed `FDP/Examples/Fdp.Examples.Scenarios/DemoBehaviorIds.cs` → `BehaviorValidationBehaviorIds.cs` as flagged in BATCH-08 review.
- No `.csproj` changes needed (no explicit compile items for this file).

---

### Task 5 — DEM1-D009 Phase A (`DistributedTankScenario`)

**Phase A delivered:**
- `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs`
  - Implements `IScenario`, `IDisposable`.
  - Creates two `DdsParticipant` instances on Domain 0 (FastCycloneDDS loopback) in `Configure`.
  - Creates a Muscle `ModuleHostKernel` internally with `SteppingTimeController`.
  - `EvaluateTick` ticks the Muscle kernel each frame and succeeds at tick 10.
  - `Dispose` releases DDS participants, Muscle kernel, and Muscle world.
- `ScenarioRegistry` entry for `ScenarioNames.DistributedTank` added.
- Two xUnit tests in `DistributedTankScenarioPhaseATests`:
  - `DistributedTank_PhaseA_RunToTick10_ExitsZero`
  - `DistributedTank_PhaseA_BothKernelsInitialized`

**Phase B follow-ups (DEM1-TASK-TRACKER D009 remains UNCHECKED):**
1. ELM handshake — `EntityLifecycleModule` on both nodes; assert `LifecycleDescriptor.State == Active` at tick 10.
2. Ghosting — `ReplicationLogicModule` on both nodes; spawn `CommandTank` (TKB 100) on Brain via `DemoTkbSetup`; assert ghost entity created on Muscle at tick 10.
3. Loco command roundtrip — write `LocomotionChannel.ActiveAction` on Brain; tick; assert ghost moves on Muscle by tick 25 (Phase 2 milestone).
4. Turret split-authority — bind `WeaponChannel` to Brain, `SimTransform` to Muscle; assert Phase 3/4 milestones.
5. Add `CarKinemToolkit` + `BehaviorToolkit` to Muscle/Brain nodes respectively.
6. DDS topics — `DemoTransformMsg`, `DemoLocomotionMsg`, `DemoWeaponMsg` translators (patterns already in `Fdp.Examples.NetworkDemo`).

---

## Debt Tracker Notes

The following DEBT-TRACKER rows should be updated by the lead after review:

| Row | New Status |
|-----|-----------|
| `LocalGridBuilderSystem` dirty path does full Clear+reinsert (BATCH-06 review, Target BATCH-09) | ✅ Resolved |
| `PerceptionScopedView.ConsumeEvents<T>` whitelist implicit (BATCH-06 review, Target BATCH-09) | ✅ Resolved (contract clarified + test) |
| `RecordingConfiguration.Blocking` missing (BATCH-07 report, Target BATCH-09+) | ✅ Resolved |
| `DemoBehaviorIds.cs` filename stale (BATCH-08 review, Target BATCH-09) | ✅ Resolved |
| DEM1-D009 DistributedTank (Target BATCH-09) | ⏳ Phase A complete; Phase B planned |
