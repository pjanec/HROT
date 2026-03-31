# BATCH-12 Report

**Batch:** BATCH-12  
**Developer:** GitHub Copilot  
**Date:** 2026-03-27  
**Status:** Complete (all 4 tasks delivered)

---

## Task Completion

| Task | Status | Notes |
|------|--------|-------|
| Task 1 — `ModuleHostKernel.Update(float)` removal in `DistributedTankScenario` | ✅ Complete | Replaced with `SteppingTimeController.Step(FixedDelta)` + `Update()` |
| Task 2 — DEM1-D009 Phase B continuation (ghost promotion, kinematics, split-authority) | ✅ Complete | Ticks 8–50: promotion, loco, turret split-authority; 7/7 tests pass |
| Task 3 — `ParallelStoriesScenario` + `RecordingModule` + `Blocking: true` | ✅ Complete | Migrated from direct `AsyncRecorder`; 3/3 ParallelStories tests pass |
| Task 4 — `LocalGridBuilderSystem` XML — `_liveByIndex` stale-slot eviction | ✅ Complete | Class summary extended with `_liveByIndex` eviction description |

---

## Testing Results

| Project | Before | After | Notes |
|---------|--------|-------|-------|
| `Fdp.Examples.Scenarios.Tests` | 56/56 | **58/58** | +2 Phase B3 locomotion + Phase B4 split-authority tests |
| `FDP.Toolkit.Replication.Tests` | 38/38 | **38/38** | No regressions |
| Solution build | Clean | **Clean** | Zero new errors or warnings in touched projects |

---

## Implementation Details

### Task 1 — `ModuleHostKernel.Update(float)` → `Update()` with explicit time step

**Change:** In `DistributedTankScenario.EvaluateTick`, replaced the obsolete
`_muscleKernel.Update(FixedDelta)` call with:

```csharp
_muscleTimeController!.Step(FixedDelta);
_muscleKernel!.Update();
```

A new field `private SteppingTimeController? _muscleTimeController` was added;
`Configure()` now stores the controller before passing it to `SetTimeController(...)`.

**Root cause discovered during Task 2 testing:** `SteppingTimeController.SeedState()`
resets `_lastDeltaTime = 0.0f`; the parameterless `Update()` returns that cached value.
Without calling `Step()`, `GlobalTime.DeltaTime = 0` propagates into the Muscle world,
causing `CarKinematicsSystem` to produce zero velocity (`BicycleModel.Integrate(dt=0)`).
The now-obsolete `Update(FixedDelta)` overload created a `GlobalTime` with `DeltaTime = FixedDelta`
directly, bypassing the controller — it worked by accident. The fix calls `Step(FixedDelta)` at the
top of each `EvaluateTick`, which is the same pattern used by `ScenarioSubsystem` for the Brain kernel.

**Files changed:**
- `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs`

---

### Task 2 — DEM1-D009 Phase B continuation

**Milestones delivered:**

| Tick | Milestone | Property | Success condition |
|------|-----------|----------|-------------------|
| 5 | Phase B1 — ELM Brain hull → Active | `PhaseBElmActive` | `lifecycle == Active` |
| 7 | Phase B2 — Ghost created on Muscle | `GhostVisibleOnMuscle` | ghost in `NetworkEntityMap` |
| 8 | Ghost promoted — TKB template applied | `_ghostPromoted` | `NavState` present; `SimTransform` authority set |
| 10 | Phase A/B checkpoint | — | `BrainInitialized && MuscleInitialized && PhaseBElmActive && GhostVisibleOnMuscle` |
| 20 | Phase B3 loco inject — `NavState.TargetSpeed = 15` | — | ghost has authority + VehicleState + VehicleParams |
| 25 | Phase B3 assert — ghost in motion | `LocoObservable` | `SimVelocity.Linear.X > 0.1` |
| 30 | Phase B4 inject — `WeaponChannel.ActiveAction = AimAndFire` on Brain Turret | — | — |
| 40 | Phase B4 Turret tracks Hull  | — | `\|turretPos − hullPos\| < 0.1 m` |
| 50 | Phase B4 split-authority — Brain Turret has weapon; Muscle ghost has physics | `SplitAuthorityActive` | `WeaponChannel.ActiveAction != 0 && SimVelocity.X > 0.1` |

**GroundKinematicsModule on Muscle:** `MuscleDirectSystemsModule` (a thin
`IEcsModule` wrapping two `IEcsModuleSystem` instances) hosts `SpatialHashSystem`
and `CarKinematicsSystem` in `Synchronous` policy (no double-buffer; runs
directly on the live Muscle world). `CarKinematicsSystem` processes entities
with `WithOwned<SimTransform>` — satisfied once `SetAuthority<SimTransform>`
is called at ghost promotion.

**Split authority (tick 50):**
- Brain kernel owns `WeaponChannel` (turret side): set via `world.SetComponent` at tick 30, no
  `ActionDispatchModule` clears it, so it persists to tick 50.
- Muscle kernel owns `SimTransform` (physics side): `SimVelocity.X` builds from
  tick 20 via `CarKinematicsSystem` after loco inject.

**New tests added to `ScenarioTests.cs`:**
- `DistributedTank_Phase2_MuscleNodeMovesOnCommand` — asserts `LocoObservable == true`
- `DistributedTank_Phase4_SplitAuthorityBothChannelsActive` — asserts `SplitAuthorityActive == true`

**Files changed:**
- `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs`
- `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs`

---

### Task 3 — `ParallelStoriesScenario` + `RecordingModule` + `Blocking: true`

**Change:** `RunLivePhase` now uses `RecordingModule` registered on the live kernel
instead of managing `AsyncRecorder` directly.

*Before:*
```csharp
// After liveKernel.Initialize()
using var recorder = new AsyncRecorder(recFilePath);
uint prevGlobalVersion = 0;
for (int t = 1; t <= LiveRunTicks; t++) {
    var stepped = liveTimeCtrl.Step(FixedDelta);
    liveKernel.Update();
    positions[(uint)t] = liveWorld.GetComponent<SimTransform>(capturedId).Position;
    long wallTicks = stepped.TotalWallTicks;
    if (t == 1) recorder.CaptureKeyframe(liveWorld, wallTicks, blocking: true);
    else        recorder.CaptureFrame(liveWorld, prevGlobalVersion, wallTicks, blocking: true);
    prevGlobalVersion = liveWorld.GlobalVersion;
}
```

*After:*
```csharp
// Before liveKernel.Initialize()
using var recordingModule = new RecordingModule(new RecordingConfiguration {
    FilePath = recFilePath,
    ExerciseId  = Guid.NewGuid(),
    Blocking = true
});
// ...
liveKernel.RegisterModule(recordingModule);
liveKernel.Initialize();
for (int t = 1; t <= LiveRunTicks; t++) {
    liveTimeCtrl.Step(FixedDelta);
    liveKernel.Update();
    positions[(uint)t] = liveWorld.GetComponent<SimTransform>(capturedId).Position;
}
```

`RecorderTickSystem` (registered in `PostSimulation` by `RecordingModule.RegisterSystems`)
handles the keyframe/delta alternation automatically (`_framesSinceKeyframe = KeyframeInterval - 1`
on construction so the very first tick is a keyframe).

**Disposal ordering:** `using var recordingModule` is declared before `using var liveKernel`
so LIFO disposal gives: `liveKernel` (stops ticks) → `recordingModule.Dispose()` (flushes LZ4 + writes
`.fdprec` manifest) → `liveWorld`.

`using Fdp.Kernel.FlightRecorder` removed from imports (no longer referenced).
Class XML summary updated to describe the new `RecordingModule`-based approach.
Tests remain 58/58 (all ParallelStories tests pass).

**Files changed:**
- `FDP/Examples/Fdp.Examples.Scenarios/Replay/ParallelStoriesScenario.cs`

---

### Task 4 — `LocalGridBuilderSystem` class XML — `_liveByIndex` stale-slot eviction

**Change:** Extended the "Index recycled at stable count" bullet in the incremental-update
strategy `<list>` within the class `<summary>` to describe the `_liveByIndex`
(`Dictionary<int, Entity>`) eviction mechanism introduced in BATCH-11:

> `_liveByIndex` detects that the slot owner has changed and calls `_grid.Remove` for the
> dead entity's last-known position before the new entity is inserted — ensuring
> `QueryNeighbors` never returns a stale dead-entity handle.

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs`

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

The most significant unexpected issue was a `DeltaTime = 0` bug introduced by Task 1's
CS0618 fix. `SteppingTimeController.SeedState()` resets `_lastDeltaTime = 0.0f`
regardless of the seed's `DeltaTime` field; `Update()` returns this cached value. The
obsolete `Update(float)` overload bypassed the controller entirely (created `GlobalTime`
with the literal argument), so the old code worked by coincidence. The fix — storing the
controller and calling `Step(FixedDelta)` before each `Update()` — matches `ScenarioSubsystem`'s
pattern for the Brain kernel, making both kernels' stepping behaviour symmetric.

The bug was identified by adding temporary `Console.Error.WriteLine` diagnostics for
`vel.X`, `hasAuthority`, `navSpeed`, and ghost-promotion state at each tick.
These were removed before submission.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

`SteppingTimeController`'s constructor silently discards the `DeltaTime` from the seed
(it only uses `TimeScale` and initializes `_lastDeltaTime = 0`). A future improvement
would be to have `SeedState()` honour `seed.DeltaTime` or add a note in the constructor's
XML that callers must call `Step()` before the first `Update()`.

`ModuleHostKernel.Dispose()` does not call `Dispose()` on `IDisposable` modules registered
via `RegisterModule(module)` (it only disposes `entry.Provider`). This requires callers
to manage `IDisposable` modules externally (as `ParallelStoriesScenario` now correctly
does with `using var recordingModule`).

**Q3: What design decisions did you make beyond the instructions?**

For `MuscleDirectSystemsModule` (Task 2), a thin `IEcsModule` wrapper with
`ExecutionPolicy.Synchronous()` was created so `SpatialHashSystem` and `CarKinematicsSystem`
run directly on the live Muscle world without a `DoubleBufferProvider`. This matches the
`AutoDriveScenario` pattern and avoids snapshot overhead in a test harness.

For Task 3 (disposal ordering), `using var recordingModule` is declared *before*
`using var liveKernel` so that the LIFO `using`-var disposal chain flushes the recorder
only after the kernel has stopped ticking — preventing a race where the recorder's
`Dispose()` might fire while `RecorderTickSystem` is still running.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

The ghost-promotion tick windows require careful ordering. `GhostPromotionSystem` runs
in `BeforeSync`, so a ghost created at tick 7 (with `TkbIdentity` set) is promoted
at tick 8 (on the *next* `_muscleKernel.Update()`). The locomotion inject at tick 20
therefore checks `_ghostPromoted` first; without promotion, `NavState` and `VehicleState`
are absent and `CarKinematicsSystem` never activates.

`RecorderTickSystem` initialises `_framesSinceKeyframe = KeyframeInterval - 1 = 59`,
so the very first `Execute()` call triggers a keyframe (tick 1) — matching the original
manual `if (t == 1) CaptureKeyframe(...)` pattern in `RunLivePhase`.

**Q5: Are there any performance concerns or optimization opportunities?**

None introduced by this batch. The Muscle kernel's `SpatialHashSystem` +
`CarKinematicsSystem` run `ForceSerial = true` in the test harness (single-threaded),
which is conservative; a production Muscle node would use parallel partitioning.

---

## Outstanding Issues / Carry-over to BATCH-13

- **DEM1-D009 Phase C:** `DemoLocomotionMsg` DDS round-trip (Brain publishes `NavState`
  command → Muscle ghost updates `NavState` via replication) not yet wired.
  The current Phase B3 loco inject writes `NavState` directly on the Muscle ghost,
  bypassing the Brain-side command path. BATCH-13 scope.
- **`DEM1-TASK-TRACKER.md` D009** remains `[ ]` pending full success-condition coverage.
