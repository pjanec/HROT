# Scenarios Test Diagnostics

Generated: 2026-07-11  
Suite: `FDP/Examples/Fdp.Examples.Scenarios.Tests`  
Run: `dotnet test ... --nologo -v n` (no BLUEPRINT_REGENERATE_SNAPSHOTS)  
Result: **43 passed / 25 failed**

---

## 1. Event-2030 Root Cause

### Which type

`RaycastRequestEvent` in `FDP/Toolkits/Fdp.Toolkits/Physics/RaycastEvents.cs` carries `[EventId(2030)]`.  
It is the unmanaged ECS event published into an EntityCommandBuffer (ECB) by background-thread physics systems to request a raycast solve.

### Where it is correctly registered (production reference)

`FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` lines 267-270:
```csharp
// Events routed via EntityCommandBuffer (must be pre-registered so ECB playback
// can call Bus.PublishRaw without hitting the "not registered" guard).
World.RegisterEvent<RaycastRequestEvent>();
World.RegisterEvent<RaycastResultEvent>();
```

### Why it is missing in the test-scenario context

**Two scenarios fail with "Event type 2030 not registered":**

1. `BallisticsAndHitScenario` (`FDP/Examples/Fdp.Examples.Scenarios/Physics/BallisticsAndHitScenario.cs`)  
   - `Configure()` calls `world.RegisterEvent<WeaponFireIntent>()` and `world.RegisterEvent<HitEvent>()`.  
   - It does NOT call `world.RegisterEvent<RaycastRequestEvent>()` or `world.RegisterEvent<RaycastResultEvent>()`.  
   - The `RaycastSolverSystem` runs inside `_physicsModule` and publishes `RaycastRequestEvent` via ECB; ECB playback throws when the event stream is not registered.

2. `UrbanCombatNewScenario` (`FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`)  
   - Same pattern: `Configure()` registers only `WeaponFireIntent` and `HitEvent`.  
   - `PhysicsToolkitModule.Initialize()` wires in `RaycastSolverSystem` which submits raycast requests via ECB.

### Exact fix location

In each scenario's `Configure()` method, after the existing `world.RegisterEvent<HitEvent>()` call, add:

```csharp
world.RegisterEvent<RaycastRequestEvent>();
world.RegisterEvent<RaycastResultEvent>();
```

Files to change (test-harness only, no production code change needed):
- `FDP/Examples/Fdp.Examples.Scenarios/Physics/BallisticsAndHitScenario.cs` — Configure(), ~line 122
- `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs` — Configure(), ~line 410

### Is this a real production defect?

**No.** The production code in `HeadlessDemoApp` (UrbanCombat) correctly registers both raycast events before any physics module ticks. The failure is a **test-harness gap**: the scenario wrappers copied the component registration but missed the raycast event registration that `HeadlessDemoApp` added at lines 269-270. This is a B=FIXTURE issue.

Affected tests (7 total): all `BallisticsAndHitScenarioTests` (4) and all `UrbanCombatNewScenarioTests` (5, minus `RunToCompletion` which also fails).  
Note: `UrbanCombatNew` has 5 failing tests but some may cascade from the same root cause.

---

## 2. Per-Cluster Failure Classification

### DistributedTank (7 failures)

| Test | Assertion | Code | Classification |
|------|-----------|------|----------------|
| `DistributedTank_PhaseA_RunToTick10_ExitsZero` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |
| `DistributedTank_PhaseB_BrainHullReachesActive_AtTick5` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |
| `DistributedTank_PhaseB_MuscleHasGhostForBrainHull` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |
| `DistributedTank_Phase2_MuscleNodeMovesOnCommand` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |
| `DistributedTank_Phase2_LocoMsgConsumedViaDds` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |
| `DistributedTank_Phase3_BrainTurretTracksHull_AtTick40` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |
| `DistributedTank_Phase4_SplitAuthorityBothChannelsActive` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |

**Root failure:** The scenario throws `ScenarioFailureException(1)` at tick 5 (Phase B Phase 1) because `GetLifecycleState(_brainHull) != EntityLifecycle.Active`. The two tests that pass (`BothKernelsInitialized`, `OnShutdown_ThenDispose_DoesNotThrow`) only check that `Configure()` ran and that Dispose is safe — they don't inspect the scenario exit code.

**Likely cause:** The `EntityLifecycleModule` zero-participant auto-promote path (`DrainInstantComplete`) is not advancing the entity from `Ghost/Constructing` to `Active` within tick 5. This could be a regression from the `EntityIndex` hot/cold rewrite (commit `7c35badb`, BATCH-02) which changed lifecycle state storage from `EntityHeader` to `EntityMetadataCold`. If the lifecycle-system reads the wrong slot or the ECB-initiated `SetLifecycleState` is not being committed before tick 5's assertion, the check fails. The ELM itself (in `FDP/Toolkits/Fdp.Toolkits/Lifecycle/`) was last touched only in the initial absorb commit.

**Verdict: C=REAL BUG — NEEDS-DECISION** (architect must verify whether ELM's `DrainInstantComplete` path is correctly hooked after the BATCH-02 EntityIndex rewrite).

---

### SensorGrid (4 failures)

| Test | Assertion | Phase | Classification |
|------|-----------|-------|----------------|
| `SensorGrid_RunToCompletion_ExitsZero` | `code==0` → code=1; `Phase1=False, Phase2=False` | — | B=FIXTURE |
| `SensorGrid_Phase1_TargetDetectedInOpenField` | `NotEqual(1, code)` → code=1 | Phase 1 tick 28 | B=FIXTURE |
| `SensorGrid_Phase2_TargetOccludedByWall` | `NotEqual(1, code)` → code=1 | Phase 1 tick 28 | B=FIXTURE |
| `SensorGrid_Phase3_TargetReacquiredAfterWall` | `Equal(0, code)` → code=1 | Phase 1 tick 28 | B=FIXTURE |

**Root cause:** `SensorGridScenario.EvaluateTick` calls a 4-stage pipeline:
1. `LocalGridBuilderSystem`
2. `VisionBroadphaseSystem` → emits `LosCheckRequestEvent`
3. `LosRequestBatchingSystem` → emits `TargetVisibleEvent`
4. `ThreatEvaluationSystem` → updates `TargetMemory`

However, `ThreatEvaluationSystem` (after a squad-coordination refactor, commit `48925daf`) **no longer reads `TargetVisibleEvent`**. It now reads from `ActiveSensorTracks` (populated by `ActiveSensorTracksUpdateSystem` which in turn reads `SensorTrackStateEvent` emitted by `SensorTrackDebounceSystem`). The missing pipeline stages are:
- `SensorTrackDebounceSystem` — converts `TargetVisibleEvent` → `SensorTrackStateEvent`
- `ActiveSensorTracksUpdateSystem` — converts `SensorTrackStateEvent` → `ActiveSensorTracks`

The scenario was never updated to include these intermediate stages, so `TargetMemory` is never written and `HasThreat()` always returns false, causing `ScenarioFailureException(1)` at tick 28.

**Fix:** The scenario's pipeline in `EvaluateTick` must add `SensorTrackDebounceSystem` between stage 3 and 4, plus `ActiveSensorTracksUpdateSystem` before `ThreatEvaluationSystem`.  
Also, the observer entity needs `SensorContactList` registered (or let `SensorTrackDebounceSystem` add it via ECB).

**Verdict: B=FIXTURE — SAFE-AUTO-FIX** (scenario pipeline needs to be extended to match the current production `AutonomousPerceptionModule` stage ordering).

---

### ComponentDamage (5 failures)

| Test | Assertion | Code | Classification |
|------|-----------|------|----------------|
| `ComponentDamage_RunToCompletion_ExitsZero` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |
| `ComponentDamage_Phase2_HealthDecreases_AfterHit` | `NotEqual(1, code)` → code=1 | 1 | C=REAL BUG |
| `ComponentDamage_Phase3_MoveFlagStripped_AfterDamage` | `NotEqual(1, code)` → code=1 | 1 | C=REAL BUG |
| `ComponentDamage_Phase4_LocomotionCleared_ByHSM` | `NotEqual(1, code)` → code=1 | 1 | C=REAL BUG |
| `ComponentDamage_Phase5_WeaponStillFires_AfterMobilityKill` | `Equal(0, code)` → code=1 | 1 | C=REAL BUG |

**Root failure:** All tests return code=1. The scenario throws `ScenarioFailureException(1)` from Phase 1 (tick 15): either `health.Current != MaxHealth` (unexpected damage before tick 20) or `CanMove == false` (capabilities pre-stripped).

The scenario's Phase 1 check (tick 15, baseline) fires BEFORE the `DetonationNotification` is injected at tick 20. For code=1 to be returned here, the APC entity must already have incorrect health or missing `CanMove` at tick 15.

**Possible causes:**
1. The `DamageCalculationSystem` or `HealthApplicationSystem` might be receiving a `DetonationNotification` from a tick 0 initial state (if ECB playback happens before the baseline check).
2. A recent change to `HealthApplicationSystem` now strips `CanMove` on non-lethal hits (lines 84-88) — but this should only fire when `health.Current < health.Max`, which requires damage first.
3. `ActorCapabilityState` initialization: the APC spawns with `CanMove | CanShoot`. The `PreviousCapabilities` shadow also has both set. If `HsmDamageBridgeSystem` (not in this scenario's module list) or any other registered system runs on world startup and mutates capabilities, that could fail phase 1.
4. It is possible the module's system pipeline runs at tick 0 (before the first `EvaluateTick` call) and some world-global system interferes.

This requires further investigation with a debugger or added logging to identify the exact state at tick 15. The failure is reproducible and consistent (not flaky), suggesting a deterministic runtime regression.

**Verdict: C=REAL BUG — NEEDS-DECISION** (root cause requires tracing why health ≠ MaxHealth or CanMove=false at tick 15, before the DetonationNotification injection at tick 20).

---

## 3. Summary Table

| Cluster | # Failing | Root Cause Summary | Classification | Action |
|---------|-----------|-------------------|----------------|--------|
| Event-2030 (BallisticsAndHit + UrbanCombatNew) | 9 | `RaycastRequestEvent` not registered in scenario Configure() | B=FIXTURE | SAFE-AUTO-FIX |
| SensorGrid | 4 | `ThreatEvaluationSystem` refactored to use `ActiveSensorTracks` but scenario pipeline still uses old `TargetVisibleEvent`→`ThreatEval` direct path | B=FIXTURE | SAFE-AUTO-FIX |
| DistributedTank | 7 | ELM zero-participant auto-promote not advancing entity to Active by tick 5 (possible BATCH-02 regression) | C=REAL BUG | NEEDS-DECISION |
| ComponentDamage | 5 | Phase 1 baseline fails (code=1 before hit injection at tick 20) — deterministic runtime regression | C=REAL BUG | NEEDS-DECISION |
