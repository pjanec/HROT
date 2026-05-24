# BATCH-07 Report — EQS Phase 5: Accurate LOS State Machine

**Tasks:** TASK-EQS-018, TASK-EQS-019
**Status:** COMPLETE
**Commit message:** `feat(eqs): accurate LOS state machine, SensorEvalState, cross-tick polling (BATCH-07)`

---

## 1. Files Created

### `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsEvalState.cs` (NEW)
Defines `EqsEvalPhase` enum (`Idle`, `Evaluating`, `_AwaitingRaycasts`, `Finalizing`) and two
structs:
- `SensorEvalState` `[ComponentId(213)]` — per-sensor cross-tick state machine
- `EqsSolverGlobalState` `[ComponentId(214)]` — shared ray-budget counters

### `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AccurateLineOfSightTest.cs` (NEW)
`ScoreExpensive` test. For each non-rejected candidate:
- Checks the `RaycastBatchData` ring buffer for an existing result keyed by
  `rayId = ((long)observer.Index << 32) | (uint)i`.
- On cache-hit: resolves immediately (`Flags |= 1` for blocked, `EntityId = -1L` for clear LOS).
- On cache-miss: submits a `RaycastRequestEvent` via command buffer (subject to per-tick budget)
  and sets `FlagPendingRay` (bit 15) on the candidate.
- Bypasses entirely when `TargetMemory` is absent, `Count == 0`, or
  `ThreatScores[0] < sensor.ThreatThreshold`.

### `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/AccurateLosTests.cs` (NEW)
Five unit tests (T-ALU1 through T-ALU4, plus T-ALU1b); all pass.

### `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/AccurateLosPhaseTests.cs` (NEW)
Three integration tests (T-ALI1, T-ALI2, T-ALI3); all pass.

---

## 2. Files Modified

### `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
Added:
```csharp
public const int SensorEvalState     = 213;
public const int EqsSolverGlobalState = 214;
```

### `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs`
Added to `SyncFrom` singleton sync block:
```csharp
SyncSingletonById(source, GlobalComponentIds.RaycastBatchData);    // AccurateLineOfSightTest ring-buffer reads
SyncSingletonById(source, GlobalComponentIds.EqsSolverGlobalState); // per-tick accurate-LOS ray budget
```
Both singletons are shared by reference between the live world and the SoD snapshot so
background solver reads and main-thread writes target the same memory.

### `Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs`
Added after the EQS Brain-tier block:
```csharp
world.RegisterComponent<SensorEvalState>();
// EQS Phase 5: RaycastRequestEvent submitted by EqsSolverSystem via cmd buffer playback;
// RaycastResultEvent published by RaycastSolverSystem (Combat/Input).
// Both must be registered so FdpEventBus.PublishRaw does not throw during harvest/flush.
world.RegisterEvent<RaycastRequestEvent>();
world.RegisterEvent<RaycastResultEvent>();
```
`RaycastResultEvent` registration was required because `RaycastSolverSystem` (part of
`SimHostCoreLogicPack.InputSystems`, registered in `EditorHarness`) publishes it via the live
world's command buffer during the Input phase, and `FdpEventBus.PublishRaw` throws if the type
has no pre-registered stream.

### `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs`
- Added `private ISimulationView _currentView = null!;` field, set in `Execute`.
- Changed all four `ExecuteBatch` call sites to pass `_currentView` instead of `repo`.
- Added `SensorEvalState` lazy-read + epoch-reset block before generation step.
- After `ScoreExpensive`, checks `anyPendingRay`:
  - `true`: sets `evalState.Phase = _AwaitingRaycasts`, writes via `_currentCmd.AddComponent`
    or `SetComponent`, returns without calling `WriteResultsToPoolAndPublish`.
  - `false`: sets `evalState.Phase = Idle`, writes via cmd buffer, proceeds to sort + write.

### `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs`
Updated `Tick` to lazy-init `EqsSolverGlobalState` singleton on the SoD and reset
`AccurateRaysSubmittedThisTick = 0` at the start of each module tick before the solver runs.

### `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`
Added optional `IEcsModuleSystem[]? extraGlobalSystems` parameter to the constructor.
Systems are registered via `Kernel.RegisterGlobalSystem` before `Kernel.Initialize()` is called.

---

## 3. Test Results

### Unit tests — `FDP/Toolkits/Fdp.Toolkits.Tests/` (40 total)

| ID       | Description                                          | Result |
|----------|------------------------------------------------------|--------|
| T-ALU1   | Ring buffer HasHit=1 -> resolved, Flags\|=1          | PASS   |
| T-ALU1b  | Ring buffer HasHit=0 -> EntityId=-1L (rejected)      | PASS   |
| T-ALU2   | Budget=0 -> FlagPendingRay set, no event submitted   | PASS   |
| T-ALU3   | Budget=2, 3 candidates -> 2 events submitted         | PASS   |
| T-ALU4   | Bypass when ThreatScore below threshold              | PASS   |
| (35 pre-existing EQS unit tests) |                              | PASS   |

**Total: 40/40 PASS**

### Integration tests — `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/` (19 total)

| ID     | Description                                                      | Result |
|--------|------------------------------------------------------------------|--------|
| T-ALI1 | Multi-tick convergence: CognitiveBuffer.IsReady true after 3+ ticks (~952ms) | PASS |
| T-ALI2 | SensorEvalState.Phase transitions to _AwaitingRaycasts on first EQS tick (~234ms) | PASS |
| T-ALI3 | IsReady false while awaiting, true after convergence (~276ms)    | PASS   |
| (16 pre-existing EQS integration tests) |                         | PASS   |

**Total: 19/19 PASS**

---

## 4. SensorEvalState Persistence

`SensorEvalState` is a per-entity component written back to the main (live) repository via the
SoD's command buffer:

```
EqsModule.Tick(SoD view)
  -> EqsSolverSystem.EvaluateSensor(entity)
     -> _currentCmd.AddComponent(entity, evalState)   // first tick: no component yet
     // or
     -> _currentCmd.SetComponent(entity, evalState)   // subsequent ticks: update existing
```

When `HarvestEntry` runs in the next `Kernel.Update()` call, `PlaybackCommands` replays the
SoD's command buffer onto `_liveWorld`, which applies the `AddComponent` or `SetComponent`
op.  On the following EQS tick, `SyncFrom` copies the `SensorEvalState` component table back
into the SoD snapshot (it is snapshotable), so `repo.HasComponent<SensorEvalState>(entity)`
returns true and the persisted phase is visible to the solver.

---

## 5. T-ALI3 Verification

T-ALI3 (`AccurateLos_NoEarlyPublish_IsReadyFalseWhileAwaiting`) verifies that the solver does
NOT publish `EqsResultEvent` while candidates have `FlagPendingRay` set.

The test:
1. Creates observer and template with `AccurateLineOfSightTest` in `ScoreExpensive`.
2. Pumps until `SensorEvalState.Phase == _AwaitingRaycasts` is visible on the main repo.
   Simultaneously asserts `EqsCognitiveBuffer.IsReady == false` (or component absent).
3. Continues pumping; after `MockRaycastSolverSystem` resolves all raycasts, the solver
   proceeds to sort + write and publishes `EqsResultEvent`.
4. `EqsResultUpdateSystem` sets `CognitiveBuffer.IsReady = true`.
5. Final assertion: `IsReady == true`.

The proof is two-part:
- **No premature publish**: the condition `IsReady == false` holds while `Phase == _AwaitingRaycasts`,
  showing `EqsResultEvent` was not published during the waiting window.
- **Eventual convergence**: `IsReady` becomes true after `MockRaycastSolverSystem` fills the
  ring buffer, confirming the solver correctly transitions from awaiting to finalizing.

---

## 6. Root-Cause Note: Event Registration

The integration tests initially failed because `RaycastRequestEvent` and `RaycastResultEvent`
were not registered in the `EditorHarness` world.  `FdpEventBus.PublishRaw` (called during
command-buffer playback) throws `InvalidOperationException` for unregistered event types.

`RaycastRequestEvent` (typeId 2030) is published by `AccurateLineOfSightTest` via the SoD cmd
buffer; during harvest, `PlaybackCommands` replays it to the live world.
`RaycastResultEvent` (typeId 2031) is published by `RaycastSolverSystem` (registered via
`SimHostCoreLogicPack.InputSystems`) to the live world's per-thread cmd buffer; the FLUSH LIVE
WORLD BUFFERS step replays it.

Both are now registered in `CognitiveComponentRegistry.RegisterAll`, which is called by
`EditorHarness`.

---

## 7. Suggested Commit Message

```
feat(eqs): accurate LOS state machine, SensorEvalState, cross-tick polling (BATCH-07)

- Add SensorEvalState[213] and EqsSolverGlobalState[214] (EqsEvalState.cs)
- Add AccurateLineOfSightTest (ScoreExpensive, deferred raycast ring buffer)
- EqsSolverSystem: anyPendingRay yield; SensorEvalState read/write via cmd buffer
- EqsModule: lazy-init EqsSolverGlobalState; reset AccurateRaysSubmittedThisTick
- EntityRepository.Sync: sync RaycastBatchData and EqsSolverGlobalState to SoD
- CognitiveComponentRegistry: register SensorEvalState, RaycastRequestEvent, RaycastResultEvent
- EditorHarness: extraGlobalSystems ctor param for test mock injection
- Tests: 5 unit tests (T-ALU1..T-ALU4) + 3 integration tests (T-ALI1..T-ALI3), 59/59 pass
```
