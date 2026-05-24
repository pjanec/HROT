# BATCH-07 — EQS Phase 5: Accurate LOS State Machine

**Tasks:** TASK-EQS-018, TASK-EQS-019
**Predecessor:** BATCH-06 committed as `c11f26ba`

---

## Mandatory Reading (read FULLY before writing any code)

1. `.dev/eqs-2/TASK-DETAIL.md` sections TASK-EQS-018 and TASK-EQS-019
2. `.dev/eqs-2/IMPLEM_DETAILS.md` lines ~1880–2100 (Phase 5 pseudocode)
3. `AGENTS.md` (workspace root) — all rules apply
4. `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` — current solver (FULL FILE)
5. `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs`
6. `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` (check next available ID after 212)
7. `Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs`
8. `FDP/Toolkits/Fdp.Toolkits/Physics/RaycastEvents.cs` — `RaycastRequestEvent` definition
9. `FDP/Toolkits/Fdp.Toolkits/Physics/Components/PhysicsComponents.cs` — `RaycastBatchData`, `RaycastHit`
10. `FDP/Toolkits/Fdp.Toolkits/Physics/PhysicsConstants.cs` — `RaycastBatchCapacity`
11. `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` — `TargetMemory`
12. `Hrot/Subsystems/Hrot.SimHost.Tests/EqsModuleTests.cs` — existing module tests (do not break)
13. `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` — harness setup
14. `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsSolverSystemPhase2Tests.cs` — pattern for integration tests
15. `FDP/Engine/Fdp.Core/Abstractions/IEntityCommandBuffer.cs` — `AddComponent`, `SetComponent`
16. `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs` — `SyncSingletonById` calls at bottom

---

## Overview

Phase 5 adds three things:

1. **`SensorEvalState` component** — per-sensor cross-tick state machine (Phase, PendingRaycastCount, etc.)
2. **`EqsSolverGlobalState` singleton** — raycast budget tracker reset each EQS module tick
3. **`AccurateLineOfSightTest`** — new `ScoreExpensive` test that submits `RaycastRequestEvent`s
4. **Modified `EqsSolverSystem`** — reads/writes `SensorEvalState` via command buffer; after
   `ScoreExpensive`, checks if any candidate has `FlagPendingRay` and yields without publishing
   `EqsResultEvent` until all raycasts are resolved

### Critical architectural constraints

- The solver MUST NEVER block: if any candidate has an unresolved raycast, return immediately
  without writing to `EqsResultPool` or publishing `EqsResultEvent`
- Minimum accurate-LOS latency: 3 EQS solver ticks (~300 ms at 10 Hz)
- `AccurateRaysSubmittedThisTick` is reset to 0 at the START of each `EqsModule.Tick`
- `SensorEvalState` is written back to the main repo via `_currentCmd.SetComponent` / `AddComponent`
  so the snapshot can sync it on the next cycle
- `RaycastBatchData` is already initialized by `PhysicsToolkitModule.Initialize(Repo)` which is
  called in `EditorHarness` constructor — do not create a second one

---

## TASK-EQS-018 — SensorEvalState component and EqsSolverGlobalState singleton

### Files to create

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsEvalState.cs`** (NEW)

```
namespace Fdp.Toolkit.Spatial.Eqs
{
    // Phase enum for the per-sensor cross-tick state machine.
    public enum EqsEvalPhase
    {
        Idle             = 0, // No evaluation in progress; ready for next query.
        Evaluating       = 1, // Running generation + filtering (unused in current impl, reserved).
        _AwaitingRaycasts = 2, // Some candidates have FlagPendingRay; waiting for ring buffer.
        Finalizing       = 3, // All raycasts resolved; sort + write next tick (reserved).
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SensorEvalState)]
    public struct SensorEvalState
    {
        // Current phase of this sensor's evaluation.
        public EqsEvalPhase Phase;

        // How many RaycastRequestEvents have been submitted but not yet resolved.
        // Used to short-circuit polling when budget was exhausted mid-batch.
        public int PendingRaycastCount;

        // Tick at which the sensor entered _AwaitingRaycasts (diagnostic).
        public uint AwaitingSinceTick;

        // Snapshot of sensor.Epoch when evaluation started.
        // If sensor.Epoch changes, evalState is reset.
        public uint CurrentEpoch;

        // Reserved for TASK-EQS-021 hot-reload.
        public uint CurrentStructureHash;
    }

    [ComponentId(GlobalComponentIds.EqsSolverGlobalState)]
    public struct EqsSolverGlobalState
    {
        // Maximum RaycastRequestEvents the EQS system is allowed to submit per EqsModule tick.
        // All sensors share this budget. Default: 2048.
        public int MaxAccurateRaycastsPerSolverTick;

        // Running count reset at the start of each EqsModule.Tick before the solver runs.
        public int AccurateRaysSubmittedThisTick;
    }
}
```

Required `using` statements: `System.Runtime.InteropServices`, `Fdp.Core`

### Files to modify

**`FDP/Engine/Fdp.Core/GlobalComponentIds.cs`** — Add after `INavmeshProvider = 212`:
```
/// <summary>Per-sensor cross-tick evaluation state (EQS v1.3 Phase 5).</summary>
public const int SensorEvalState = 213;
/// <summary>Global EQS solver budget singleton (EQS v1.3 Phase 5).</summary>
public const int EqsSolverGlobalState = 214;
```

**`Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs`** — In `RegisterAll`, after the
EQS Brain-tier block, add:
```csharp
world.RegisterComponent<SensorEvalState>();
```
(EqsSolverGlobalState is a singleton, not a per-entity component; no RegisterComponent needed.)

### Success conditions
- `SensorEvalState` compiles as unmanaged struct with `[ComponentId(213)]`
- Unit test: set/read `EqsSolverGlobalState.AccurateRaysSubmittedThisTick` correctly
- Build succeeds

---

## TASK-EQS-019 — AccurateLineOfSightTest and cross-tick polling in EqsSolverSystem

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AccurateLineOfSightTest.cs`

Phase: `ScoreExpensive`

Behaviour:
1. Cast `view` to `EntityRepository`. If not possible, return.
2. Read primary threat from `TargetMemory` on the observer:
   - If `!repo.HasComponent<TargetMemory>(observer)` OR `mem.Count == 0` OR
     `mem.ThreatScores[0] < sensor.ThreatThreshold` → return (bypass, all candidates pass).
3. Guard: if `!repo.HasSingleton<RaycastBatchData>()` → set `FlagPendingRay` on all
   non-rejected candidates (so the solver knows to wait) and return.
4. Get `ref readonly var rayBatch = ref repo.GetSingleton<RaycastBatchData>()`.
5. Get `ref var globalState = ref repo.GetSingletonUnmanaged<EqsSolverGlobalState>()`.
   If `!repo.HasSingleton<EqsSolverGlobalState>()` → return.
6. Threat position: `targetPos3D = new Vector3(mem.PositionsX[0], mem.PositionsY[0], 1.5f)`.
7. Get cmd via `view.GetCommandBuffer()`.
8. For each candidate at index `i`:
   - Skip if `candidates[i].EntityId == -1L`.
   - Compute `long rayId = ((long)observer.Index << 32) | (uint)i`.
   - Compute `int slot = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity)`.
   - Read `ref readonly var hit = ref rayBatch.Hits[slot]`.
   - **If `hit.RayId == rayId`** (result already in ring buffer):
     - Clear `FlagPendingRay`: `candidates[i].Flags &= (short)~FlagPendingRay`.
     - If `hit.HasHit != 0`: reject → `candidates[i].EntityId = -1L` (exposed to threat).
     - Else: mark occluded → `candidates[i].Flags |= 1` (flag bit 0 = good cover).
     - Continue to next candidate.
   - **Else** (not in ring buffer yet):
     - If `globalState.AccurateRaysSubmittedThisTick < globalState.MaxAccurateRaycastsPerSolverTick`:
       - Publish `RaycastRequestEvent`:
         ```
         new RaycastRequestEvent
         {
             Start        = new Vector3(candidates[i].PositionX, candidates[i].PositionY, 1.5f),
             End          = targetPos3D,
             RayId        = rayId,
             Observer     = observer,
             Target       = Entity.Null,  // positional query: no target entity
             LayerMask    = -1,           // hit all geometry
             IgnoreEntity = observer,
             SourceNodeId = 0,
         }
         ```
       - `globalState.AccurateRaysSubmittedThisTick++`.
     - Mark pending: `candidates[i].Flags |= (short)FlagPendingRay`.

Public constants:
```csharp
/// <summary>Flag bit 15: raycast submitted but result not yet in ring buffer.</summary>
public const short FlagPendingRay = 1 << 15;
```

Note: `EqsResult.Flags` is a `short`. The cast `(short)FlagPendingRay` is correct since
`1 << 15 = 32768 = short.MinValue` in two's-complement (fits in short bit pattern).

Use `unsafe` on `ExecuteBatch` if needed to access `TargetMemory` fixed arrays.

### Modified: `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs`

Changes required:

**1. New field**: `private ISimulationView _currentView = null!;`
   (needed to pass to test's `ExecuteBatch` as `view`)

Set it in `Execute`: `_currentView = view;` alongside `_currentCmd = view.GetCommandBuffer()`.

**2. `EvaluateSensor` — add SensorEvalState management BEFORE the generation step**:

```csharp
// --- SensorEvalState management ---
// Lazy-add SensorEvalState via command buffer if not yet on this entity.
SensorEvalState evalState;
if (repo.HasComponent<SensorEvalState>(entity))
    evalState = repo.GetComponentRO<SensorEvalState>(entity);
else
    evalState = new SensorEvalState { Phase = EqsEvalPhase.Idle, CurrentEpoch = sensor.Epoch };

// Reset on epoch change (sensor parameters changed → discard in-flight raycasts).
if (evalState.CurrentEpoch != sensor.Epoch)
{
    evalState = new SensorEvalState { Phase = EqsEvalPhase.Idle, CurrentEpoch = sensor.Epoch };
}
```

**3. Replace the test execution loops** to pass `_currentView` instead of `repo`:

```csharp
if (template.FilterCheap != null)
    foreach (var test in template.FilterCheap)
        test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), _currentView, activeCandidates);

// ... same for FilterExpensive, ScoreCheap, ScoreExpensive
```

(Currently they pass `repo`; change to `_currentView` in ALL four test loops.)

**4. After ScoreExpensive, BEFORE the sort**, add the pending-ray check:

```csharp
// Check if any candidate has FlagPendingRay set.
// If so, we cannot write to pool yet — yield and wait for ring buffer results.
bool anyPendingRay = false;
for (int i = 0; i < activeCandidates.Length; i++)
{
    if ((activeCandidates[i].Flags & AccurateLineOfSightTest.FlagPendingRay) != 0)
    {
        anyPendingRay = true;
        break;
    }
}

if (anyPendingRay)
{
    evalState.Phase = EqsEvalPhase._AwaitingRaycasts;
    evalState.AwaitingSinceTick = _currentTick;
    // Write SensorEvalState back to main repo via command buffer.
    if (repo.HasComponent<SensorEvalState>(entity))
        _currentCmd.SetComponent(entity, evalState);
    else
        _currentCmd.AddComponent(entity, evalState);
    return; // DO NOT publish EqsResultEvent while awaiting raycasts.
}

// All raycasts resolved (or no AccurateLOS test in template): proceed to sort + write.
evalState.Phase = EqsEvalPhase.Idle;
if (repo.HasComponent<SensorEvalState>(entity))
    _currentCmd.SetComponent(entity, evalState);
else
    _currentCmd.AddComponent(entity, evalState);
```

Keep the sort and `WriteResultsToPoolAndPublish` call immediately after (no changes to them).

**5. After the `count == 0` early return** (Phase 1 fallback and empty generation), also write
back evalState with `Phase = Idle` using the same pattern, to ensure stale `_AwaitingRaycasts`
phase does not persist when sensor has no results.

Note: the fallback paths (no registry, unknown template, count==0) do NOT need to persist
evalState since they also reset the phase by publishing an empty event.

### Modified: `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs`

Change `Tick` to:

```csharp
public void Tick(ISimulationView view, float deltaTime)
{
    if (view is EntityRepository repo)
    {
        // Lazy-init global solver state.
        if (!repo.HasSingleton<EqsSolverGlobalState>())
            repo.SetSingletonUnmanaged(new EqsSolverGlobalState
            {
                MaxAccurateRaycastsPerSolverTick = 2048,
                AccurateRaysSubmittedThisTick    = 0,
            });

        // Reset per-tick ray budget at the start of each module tick.
        ref var gs = ref repo.GetSingletonUnmanaged<EqsSolverGlobalState>();
        gs.AccurateRaysSubmittedThisTick = 0;
    }
    _solver.Execute(view, deltaTime);
}
```

(Preserve the `private readonly EqsSolverSystem _solver = new();` line unchanged.)

---

## Test Plan

### Unit tests — `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/AccurateLosTests.cs` (NEW)

**T-ALU1: Ring buffer already has result → candidate resolved, no new event**

Setup:
- Plain `EntityRepository` (no EditorHarness).
- Register components needed: `EqsSensor`, `EqsCognitiveBuffer`, `SensorEvalState`, `TargetMemory`.
- Register events: `RaycastRequestEvent`.
- Create observer entity; add `EqsSensor { ThreatThreshold = 0 }`, `SensorEvalState`, and
  `TargetMemory` with one threat: `Count=1, ThreatScores[0]=100, PositionsX[0]=10, PositionsY[0]=0`.
- Create `RaycastBatchData` singleton with persistent NativeArray (dispose after test).
- Pre-fill ring buffer: compute `rayId = ((long)observer.Index << 32) | 0u`. Set
  `batch.Hits[(int)((uint)rayId % capacity)] = new RaycastHit { RayId = rayId, HasHit = 0 }`.
  (`HasHit=0` = no hit = clear LOS = candidate is exposed = good cover... wait: clear LOS means
  the threat CAN see the candidate → the candidate is exposed → NOT good cover → REJECT).
  Wait, re-read: `AccurateLineOfSightTest` checks if the threat has LOS to the candidate.
  If `HasHit = 0` (no geometry blocking) → LOS clear → candidate exposed → `EntityId = -1L`.
  If `HasHit = 1` (geometry blocks) → LOS blocked → candidate occluded → `Flags |= 1` (good).
  For T-ALU1: use `HasHit = 1` so the candidate survives.
- Create `EqsSolverGlobalState { MaxAccurateRaycastsPerSolverTick=2048 }` singleton.
- Span: `Span<EqsResult> candidates = stackalloc EqsResult[1]`.
- Set `candidates[0] = new EqsResult { PositionX = 5, PositionY = 0, EntityId = 0 }` (positional).
- Run `new AccurateLineOfSightTest().ExecuteBatch(observer, ref sensor, repo, candidates)`.
- Assert: `candidates[0].EntityId != -1L` (not rejected).
- Assert: `(candidates[0].Flags & 1) != 0` (flag bit 0 set = occluded).
- Assert: `(candidates[0].Flags & AccurateLineOfSightTest.FlagPendingRay) == 0` (resolved).
- Assert: 0 `RaycastRequestEvent`s published.

Cleanup: dispose NativeArray.

**T-ALU2: Budget=0 → FlagPendingRay set, no event submitted**

Setup (similar to T-ALU1 but ring buffer slot does NOT have the matching RayId):
- Pre-fill ring buffer with `new RaycastHit { RayId = 0 }` (does not match computed rayId).
- `EqsSolverGlobalState { MaxAccurateRaycastsPerSolverTick = 0 }` (budget exhausted).
- Run `AccurateLineOfSightTest.ExecuteBatch(...)`.
- Assert: `(candidates[0].Flags & AccurateLineOfSightTest.FlagPendingRay) != 0`.
- Assert: 0 `RaycastRequestEvent`s published (budget=0, nothing submitted).

**T-ALU3: Budget=2 submits exactly 2 events for 3 candidates**

Setup:
- Budget=2, ring buffer empty (no matching RayIds), 3 candidates.
- Run ExecuteBatch.
- Count `RaycastRequestEvent`s via `repo.ReadEvents<RaycastRequestEvent>()`.
- Assert: count == 2.
- Assert: candidates[0].Flags has FlagPendingRay (submitted).
- Assert: candidates[1].Flags has FlagPendingRay (submitted).
- Assert: candidates[2].Flags has FlagPendingRay (NOT submitted — budget exhausted, but still marked).

**T-ALU4: Bypass when threat below threshold**

Setup:
- `TargetMemory { Count=1, ThreatScores[0]=10 }`, sensor `ThreatThreshold=50`.
- No ring buffer setup needed.
- Run ExecuteBatch.
- Assert: no candidate has FlagPendingRay, no events published.

### Integration test — `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/AccurateLosPhaseTests.cs` (NEW)

**[Collection("EqsIntegrationTests")]** — mandatory on the class.

**Setup helpers in this test class:**

```csharp
// MockRaycastSolverSystem: consumes RaycastRequestEvent and writes to ring buffer.
// HasHit=1 (blocked) by default so all candidates survive as good cover.
private sealed class MockRaycastSolverSystem : IEcsModuleSystem
{
    public int RaycastsResolvedTotal { get; private set; }
    public int RaycastsResolvedLastTick { get; private set; }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        if (!repo.HasSingleton<RaycastBatchData>()) return;

        ref var batch = ref repo.GetSingleton<RaycastBatchData>();
        var events    = view.ReadEvents<RaycastRequestEvent>();
        RaycastsResolvedLastTick = 0;

        for (int i = 0; i < events.Length; i++)
        {
            ref readonly var evt = ref events[i];
            int slot = (int)((uint)evt.RayId % (uint)PhysicsConstants.RaycastBatchCapacity);
            batch.Hits[slot] = new RaycastHit
            {
                RayId  = evt.RayId,
                HasHit = 1, // blocked = candidate is occluded = good cover
            };
            RaycastsResolvedLastTick++;
            RaycastsResolvedTotal++;
        }
    }
}
```

**Shared setup:**

- Create a `SimpleEqsTemplateRegistry` (copy the inner class from `EqsSolverSystemPhase2Tests`).
- Register a test template with `BlueprintId = 99u` using `NavmeshSamplesGenerator` + `AccurateLineOfSightTest`:
  ```csharp
  var losTest = new AccurateLineOfSightTest();
  registry.Register(new EqsQueryTemplate
  {
      BlueprintId      = 99u,
      Generator        = new NavmeshSamplesGenerator(),
      ScoreExpensive   = new IEqsTest[] { losTest },
      MaxCandidates    = 8,
  });
  ```
- Use `StubNavmeshProvider` (returns 5 sample points via `GetRandomPointsInRadius`).
  Wait — `StubNavmeshProvider.GetRandomPointsInRadius` returns a 3x3 grid limited to the span size.
  With `MaxCandidates=8`, you get min(8, 9) = up to 9 samples but capped by span. Adjust:
  use `MaxCandidates = 5` and verify the stub returns exactly 5 (or fewer). Check the stub's implementation.
  If the stub returns fewer than 5, just use fewer candidates (3 is sufficient: budget=2, 2 ticks to resolve all 3).
  **Adjust the test to use budget=2, MaxCandidates=4** so the multi-tick scenario requires 2 EQS solver ticks to resolve all 4 candidates (2 raycasts tick 1, 2 raycasts tick 2, all resolved tick 3).

- Create observer entity with:
  - `SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity }`
  - `NetworkIdentity` (required by solver query)
  - `TargetMemory` with one threat: `Count=1, ThreatScores[0]=100, PositionsX[0]=30, PositionsY[0]=0`
  - `EqsSensor { BlueprintId=99u, Epoch=1, SearchRadius=50f, ThreatThreshold=0f }`
- Inject singletons into `_harness.Repo`:
  - `IEqsTemplateRegistry` via `SetSingletonManaged`
  - `INavmeshProvider` via `SetSingletonManaged` (use `StubNavmeshProvider`)
  - `EqsSolverGlobalState { MaxAccurateRaycastsPerSolverTick=2 }` via `SetSingletonUnmanaged`
    (override the default 2048 budget for test control)
- Register and add `MockRaycastSolverSystem` via `_harness.Kernel.RegisterGlobalSystem(mockSolver)`
  (use Input or Simulation phase — check how other systems are registered in the harness).

**T-ALI1: Multi-tick convergence — IsReady true after 3+ EQS solver ticks**

```csharp
// Pump until CognitiveBuffer.IsReady == true.
// With budget=2 and 4 candidates, requires at minimum:
//   EQS tick 1: submit 2 raycasts → MockSolver resolves → Phase=_AwaitingRaycasts
//   EQS tick 2: 2 resolved from buffer, submit 2 more → MockSolver resolves → Phase=_AwaitingRaycasts
//   EQS tick 3: all 4 resolved → sort+write → IsReady=true
// PumpUntil with 5000ms timeout.
bool ready = _harness.PumpUntil(() =>
    _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer) &&
    _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady,
    5000);
Assert.True(ready, "CognitiveBuffer.IsReady did not become true within timeout.");
```

Also assert `CognitiveBuffer.Count > 0` (candidates survived HasHit=1).

**T-ALI2: Phase is _AwaitingRaycasts after first EQS solver tick (before all resolved)**

This requires reading `SensorEvalState` from the main repo AFTER the first EQS tick but
BEFORE all raycasts resolve.

Strategy: DISABLE the `MockRaycastSolverSystem` for this assertion (or use a delayed resolver).

Alternative (simpler): just assert that `CognitiveBuffer.IsReady == false` immediately after
the first EQS solver tick fires. Then re-enable the mock and let it converge.

Implementation:
- Set `MockRaycastSolverSystem.Enabled = false` initially (add an `Enabled` flag).
- Pump until 1 EQS tick fires (detect via solver tick counter or `RaycastsResolvedTotal > 0`... but if disabled, count stays 0. Better: detect by waiting for first `SensorEvalState` change on main repo).
- Actually: pump with small timeout, then assert `SensorEvalState.Phase == _AwaitingRaycasts`
  if `SensorEvalState` is on the main repo.

Simpler alternative that avoids the race condition:
```csharp
// Assert that the solver does NOT immediately publish EqsResultEvent
// before all raycasts are resolved (indirectly proven by T-ALI1's multi-tick requirement).
// Direct Phase assertion via SensorEvalState on main repo after first EQS tick:
bool phaseAwaitingReached = _harness.PumpUntil(() =>
{
    if (!_harness.Repo.HasComponent<SensorEvalState>(observer)) return false;
    return _harness.Repo.GetComponentRO<SensorEvalState>(observer).Phase
           == EqsEvalPhase._AwaitingRaycasts;
}, 5000);
Assert.True(phaseAwaitingReached, "SensorEvalState did not reach _AwaitingRaycasts.");
```

(This works because: on EQS tick 1, the solver writes Phase=_AwaitingRaycasts via cmd buffer.
The cmd buffer is flushed to main repo between ticks. PumpUntil will eventually see it.)

**T-ALI3: Solver does NOT publish EqsResultEvent while _AwaitingRaycasts**

This is proven indirectly: if the solver published EqsResultEvent while awaiting,
`EqsResultUpdateSystem` would set `IsReady=true` immediately (within 1 EQS tick).
But T-ALI1 shows it takes 3+ ticks. If the solver were publishing prematurely, T-ALI1 would
succeed in 1 tick.

You can make this explicit by using a mock that tracks events:
```csharp
// Before enabling MockRaycastSolverSystem, pump for 2 EQS ticks with mock disabled.
// Assert CognitiveBuffer.IsReady == false the whole time.
// Then enable mock and assert convergence.
```

### Cleanup in test Dispose()

After each test:
1. Dispose `EqsResultPool` NativeArray (same as Phase2Tests).
2. Do NOT dispose `RaycastBatchData` — that is owned by `PhysicsToolkitModule` in the
   EditorHarness and is disposed when the harness disposes.

---

## Build and Test

```bash
# Build
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet build IOS-IG-SimHost.sln

# Unit tests
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs" --verbosity normal

# Integration tests
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs" --verbosity normal

# Regression: SimHost unit tests (allow pre-existing failures, do not introduce new ones)
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/ --verbosity normal 2>&1 | tail -5
```

All new tests must PASS. Pre-existing failures in SimHost.Tests are acceptable (known ~32).

---

## Report

Write `.dev/eqs-2/reports/BATCH-07-REPORT.md` with:
1. Summary of all files created/modified
2. Test results: each test ID + PASS/FAIL
3. Confirmation that `SensorEvalState` persists across EQS solver ticks via command buffer
4. Explanation of how T-ALI3 is verified
5. Suggested commit message: `feat(eqs): accurate LOS state machine, SensorEvalState, cross-tick polling (BATCH-07)`

---

## Key constraints summary

- `FlagPendingRay = (short)(1 << 15)` — fits `EqsResult.Flags` (short)
- Bypass `AccurateLineOfSightTest` if `TargetMemory.Count == 0` or score below threshold
- In `EvaluateSensor`: pass `_currentView` (not `repo`) to all `ExecuteBatch` calls
- `SensorEvalState` persists via `_currentCmd.AddComponent` / `SetComponent` (not direct write)
- Epoch mismatch resets `evalState` to Idle and updates `CurrentEpoch`
- `EqsSolverGlobalState` is reset in `EqsModule.Tick` BEFORE calling solver (per EQS tick, not per sensor)
- The `ReduceTopK` must happen BEFORE the `anyPendingRay` check (same position as current code)
- Do NOT call `WriteResultsToPoolAndPublish` when `anyPendingRay == true`
- Budget check: `AccurateRaysSubmittedThisTick < MaxAccurateRaycastsPerSolverTick` (strict less-than)
- `_currentCmd` is already used in EqsSolverSystem for `PublishEvent`; extend with `AddComponent`/`SetComponent`
