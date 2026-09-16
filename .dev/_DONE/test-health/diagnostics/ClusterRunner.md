# ClusterRunner Test Health Diagnostics

**Date:** 2026-07-12  
**Branch:** main  
**Build:** Both projects compile cleanly (0 errors, only DDS topic-descriptor warnings in NED).

---

## Summary

| Project | Total | Passed | Failed | Clusters |
|---------|-------|--------|--------|---------|
| Hrot.ClusterRunner.Tests | 251 | 234 | **17** | 2 |
| Hrot.ClusterRunner.Integration.Tests | ~200+ (run aborted by DDS crash) | varies | **≥ 30** | 3 |

**Total failure clusters: 5** (2 in unit tests, 3 in integration tests).

> Note: The integration test run is aborted mid-suite by an unhandled `CycloneDDS.Runtime.DdsException: dds_take failed: -3 (ReturnCode: BadParameter)` that crashes the test host process. This makes the total failure count non-deterministic between runs. All root-cause analysis below is based on observed failures before the crash.

---

## Cluster 1 — Nav Module Ordering Bug (C: Real Production Bug)

**Affects:** `Hrot.ClusterRunner.Tests` and `Hrot.ClusterRunner.Integration.Tests`

### Exception

```
System.InvalidOperationException: Call RegisterSystems before RegisterProviders.
  at Fdp.Toolkit.Navigation.EngineBacked.EngineBackedNavigationModule.RegisterProviders(EntityRepository repo)
     in FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedNavigationModule.cs:line 64
  at Hrot.SimHost.SimHostNodeBootstrapper.RegisterSpawningPipeline(HrotNodeContext context)
     in Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs:line 294
  at Hrot.Common.Infrastructure.SharedApplicationBootstrapper.BootstrapNode(...)
     in Hrot/Engine/Hrot.Common/Infrastructure/SharedApplicationBootstrapper.cs:line 109
```

### Root Cause

`SimHostNodeBootstrapper.RegisterSpawningPipeline` (line 293–294) calls:

```csharp
context.Kernel.RegisterModule(navModule);
navModule.RegisterProviders(context.World);   // ← BUG
```

`RegisterProviders` checks `if (_navmesh == null || _registry == null)` and throws, because `RegisterSystems` (which sets those fields) is only called during `Kernel.Initialize()` — Phase 7 — while `RegisterSpawningPipeline` is called in Phase 6a, before `Initialize()`.

The production file is:  
`Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs:line 293`  
The guard is in:  
`FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedNavigationModule.cs:line 63`

### Failing Tests

**ClusterRunner.Tests — SimHostSubsystemTests (15 tests)**

| Test | Class | Proposed Fix |
|------|-------|-------------|
| `Initialize_CreatesKernelAndModules_WithoutException` | C | See below |
| `Initialize_RegistersCycloneNetworkCleanupSystem` | C | See below |
| `Initialize_NonZeroDomain_PassedThrough` | C | See below |
| `Initialize_NodeIdZero_DoesNotThrow` | C | See below |
| `Initialize_NodeIdNonZero_DoesNotThrow` | C | See below |
| `Initialize_DomainZero_DoesNotThrow` | C | See below |
| `Initialize_DomainZero_PassesDomainZeroToApp` | C | See below |
| `DrawWorld_IsAlwaysNoOp` | C | See below |
| `DrawUI_Headless_DoesNotThrow` | C | See below |
| `Update_AfterInit_TicksKernelWithoutException` | C | See below |
| `Update_MultipleFrames_AccumulatesWithoutError` | C | See below |
| `Shutdown_AfterInit_ReleasesResources` | C | See below |
| `FullLifecycle_Headless_CompletesCleanly` | C | See below |
| `Start_StartsBackgroundThread` | C | See below |
| `Start_CalledTwice_DoesNotDoubleStart` | C | See below |

**ClusterRunner.Integration.Tests — all tests via HrotRunnerHarness (≥ 20 tests)**

All integration tests that construct `HrotRunnerHarness` or call `SimHostSubsystem.Initialize` inherit this failure. Partial list observed before the test-host crash:

- `SplitAuthoritySpawnTests.*` (4 tests)
- `MapPlacementIntegrationTests.*` (2 tests)
- `SpawnMovingVehicleWithGatewayIntegrationTests.*` (1 test)
- `EntityDestroyIntegrationTests.*` (2 tests)
- `SubEntityCascadeDestroyTests.*` (1 test)
- `NetworkGatewayIntegrationTests.*` (1 test)
- `AllSubsystemsClusterTransitionTests.*` (1 test)
- `AclBackdoorEliminationTests.AreaAuthoring_EndToEnd_*` (1 test)
- `TimeControlIntegrationTests.*` (6 tests — all crash in harness constructor)
- `DistributedBrainMuscleIntegrationTests.*` (3 tests)
- `MiniExConIntegrationTests.*` (4+ tests)
- `MissionControlIntegrationTests.*`, `SelectionAndMissionIntegrationTests.*`, `DragDropIntegrationTests.*`, `SensorMechanismIntegrationTests.*` (≥ 7 more tests)

### Proposed Fix

In `SimHostNodeBootstrapper.RegisterSpawningPipeline`, move `navModule.RegisterProviders` to AFTER `Kernel.Initialize()` is called. The correct pattern is a two-step: register the module now, call `RegisterProviders` in a post-init hook.

Two options:
1. **Call `navModule.RegisterSystems` manually** before `RegisterProviders` (explicit, but duplicates what `Kernel.Initialize()` does — risky if `RegisterSystems` has side effects on the kernel's system registry).
2. **Refactor `EngineBackedNavigationModule`** so `RegisterProviders` is called automatically inside `Kernel.Initialize()` via an `IInitializableModule` callback, or merge providers into `RegisterSystems`.
3. **Simplest safe fix**: inside `RegisterSpawningPipeline`, call `navModule.RegisterSystems(kernelRegistry)` on a local adapter, then call `RegisterProviders`. This requires exposing a way to call `RegisterSystems` without going through the kernel.

**Judgment**: Option 2 (merging into `RegisterSystems`) is the cleanest. `RegisterProviders` only uses fields set by `RegisterSystems`, so it can be folded in. Alternatively, `RegisterSystems` can call `RegisterProviders` directly on the world if a world reference is passed to the constructor.

| SAFE-AUTO-FIX or NEEDS-DECISION |
|----------------------------------|
| **NEEDS-DECISION** — the fix touches the `EngineBackedNavigationModule` contract and its relationship to the bootstrapper's phase ordering. |

---

## Cluster 2 — DataDrivenGizmoSystem Hard-Casts IDebugDrawBuilder (A: Stale Test)

**Affects:** `Hrot.ClusterRunner.Tests` only

### Exception

```
System.InvalidCastException: Unable to cast object of type
'Hrot.ClusterRunner.Tests.D003NoOpDrawBuilder' to type
'Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer'.
  at Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem.Execute(ISimulationView view, float deltaTime)
     in FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs:line 314
```

### Root Cause

The test `DataDrivenGizmoPredicateTests` passes a test-only `D003NoOpDrawBuilder` (which implements `IDebugDrawBuilder`) as the `drawBuilder` constructor parameter of `DataDrivenGizmoSystem`. The production constructor accepted `IDebugDrawBuilder`, so this was valid.

However, production code was subsequently changed to hard-cast `_drawBuilder` to `DebugPrimitiveBuffer` at lines 314, 370, 372, 402, and 404 of `DataDrivenGizmoSystem.cs` to call `buf.Count` and `buf.StampGizmoTypeId(mark, ...)`. These methods do not exist on `IDebugDrawBuilder` — they are only on the concrete `DebugPrimitiveBuffer` class.

The test was never updated to use a `DebugPrimitiveBuffer` instance (or a subtype of it).

- Production file: `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs:314`
- Test file: `Hrot/Runner/Hrot.ClusterRunner.Tests/DataDrivenGizmoPredicateTests.cs:123,154`

### Failing Tests

| Test | Class | Proposed Fix | SAFE-AUTO-FIX |
|------|-------|-------------|----------------|
| `D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity` | A | Replace `D003NoOpDrawBuilder draw` with `new DebugPrimitiveBuffer(capacity)` (or a minimal subclass of `DebugPrimitiveBuffer` if it has no zero-arg ctor) | **SAFE-AUTO-FIX** |
| `D003_Predicate_True_AllowsUpdateAndDraw` | A | Same | **SAFE-AUTO-FIX** |

The test spec (predicate filtering behavior) is still correct. Only the test double (`D003NoOpDrawBuilder`) needs replacing with a `DebugPrimitiveBuffer` instance.

---

## Cluster 3 — EditorHarness BarrierPending Lookahead Burns Blueprint Frames (B: Fixture Gap)

**Affects:** `Hrot.ClusterRunner.Integration.Tests` only

### Root Cause

`EditorHarness` constructor calls `_timeController.SwitchToDeterministic(new HashSet<int>())`, which sets the `MasterSyncController` to `BarrierPending` mode with a barrier at `now + LookaheadWallTicks`. The default `LookaheadWallTicks` is **200 ms** (`TimeConfig.Default`).

`EditorHarness.PumpFrames`:
```csharp
_timeController?.Step(PumpSleepMs / 1000f);   // Step() is no-op if mode != Stepping
Kernel.Update();                                // Update() transitions BarrierPending → Stepping
                                               // once real-clock crosses barrier, else returns dt=0
```

For each call to `PumpFrames(N)`:
- While the real clock has not yet crossed the 200ms barrier, `Step()` is always a silent no-op, `Kernel.Update()` returns `deltaTime=0`, and `BlueprintTickSystem.Execute` skips execution (`if (deltaTime <= 0f) return`).
- Once the barrier clears (after ≥200ms real wall time), frames begin producing `deltaTime > 0` and blueprints tick.

Tests that complete in under 200ms total will observe **count = 0** (no ticks), while tests that take longer show partial counts. This explains:
- `CaptureLiveState_AfterNFrames_CountEqualsN`: all N give `actual = 0`
- `InstanceBlueprint_TicksInRealKernel_CounterAdvancesByFrameCount(frames: 3)`: gives `2` (1 frame burned to BarrierPending, 2 frames tick before the test assertion)
- `AttachToEntity_IsIdempotent_DoesNotDoubleCountInRealKernel(4 frames)`: gives `3`

- Harness file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs:152`
- Controller: `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs:252` (`_config.LookaheadWallTicks`)
- Config default: `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/TimeConfig.cs:65`

### Failing Tests

| Test | Class | Proposed Fix | SAFE-AUTO-FIX |
|------|-------|-------------|----------------|
| `BlueprintKernelRunTests.InstanceBlueprint_TicksInRealKernel_CounterAdvancesByFrameCount(frames: 1)` | B | See below | **SAFE-AUTO-FIX** |
| `BlueprintKernelRunTests.InstanceBlueprint_TicksInRealKernel_CounterAdvancesByFrameCount(frames: 3)` | B | See below | **SAFE-AUTO-FIX** |
| `BlueprintKernelRunTests.InstanceBlueprint_TicksInRealKernel_CounterAdvancesByFrameCount(frames: 10)` | B | See below | **SAFE-AUTO-FIX** |
| `BlueprintKernelRunTests.InstanceBlueprint_TwoEntities_AdvanceIndependentlyInRealKernel` | B | See below | **SAFE-AUTO-FIX** |
| `BlueprintKernelRunTests.AttachToEntity_IsIdempotent_DoesNotDoubleCountInRealKernel` | B | See below | **SAFE-AUTO-FIX** |
| `BlueprintObserveTests.CaptureLiveState_AfterNFrames_CountEqualsN(frames: 1)` | B | See below | **SAFE-AUTO-FIX** |
| `BlueprintObserveTests.CaptureLiveState_AfterNFrames_CountEqualsN(frames: 3)` | B | See below | **SAFE-AUTO-FIX** |
| `BlueprintObserveTests.CaptureLiveState_AfterNFrames_CountEqualsN(frames: 5)` | B | See below | **SAFE-AUTO-FIX** |

**Proposed Fix:** Pass a `TimeConfig` with `LookaheadWallTicks = 0` when creating the `MasterSyncController` in `EditorHarness`, or call `_timeController.SwitchToContinuous()` before `PumpFrames`. Alternatively, add a `WarmupFrames` call in `EditorHarness` constructor that pumps enough frames to let the barrier clear, matching the existing pattern in `HrotRunnerHarness`. The safest no-change-to-production fix is to inject `TimeConfig` with zero lookahead into the harness constructor.

---

## Cluster 4 — CaptureLiveState Returns Fields Without DebugMap (A: Stale Test)

**Affects:** `Hrot.ClusterRunner.Integration.Tests` only

### Root Cause

`BlueprintObserveTests.CaptureLiveState_WithoutDebugMap_ReturnsSnapshotWithEmptyFields` asserts `Assert.Empty(snapshot.FieldValues)` when no `DebugMap` is registered. However, `BlueprintDebugSession.ReadInstanceState` (line 1390–1399 in `BlueprintDebugSession.cs`) has a fallback path that uses `def.StateFields` when `stateLayout` is null:

```csharp
else if (def?.StateFields is { Count: > 0 } stateFields)
{
    foreach (var (name, descriptor) in stateFields)
    {
        ...
        outFields[name] = raw;
    }
}
```

`CounterDemoBlueprint.MakeDefinition()` populates `StateFields` with `{"Count": ...}`, so even without a registered DebugMap, `CaptureLiveState` returns non-empty `FieldValues`. The test spec was written when this fallback did not exist or when `CounterDemoBlueprint` had empty `StateFields`.

- Production file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs:1390`
- Test file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BlueprintObserveTests.cs:134`

### Failing Tests

| Test | Class | Proposed Fix | SAFE-AUTO-FIX |
|------|-------|-------------|----------------|
| `BlueprintObserveTests.CaptureLiveState_WithoutDebugMap_ReturnsSnapshotWithEmptyFields` | A | Update assertion to `Assert.NotEmpty(snapshot.FieldValues)`, OR if the intention is that `StateFields` fallback should NOT populate fields without a DebugMap, add a flag to disable fallback | **NEEDS-DECISION** — depends on intended API contract |

---

## Cluster 5 — CycloneDDS Test Host Crash (C: Real Production Bug)

**Affects:** `Hrot.ClusterRunner.Integration.Tests` only

### Exception

```
Unhandled exception. CycloneDDS.Runtime.DdsException: dds_take failed: -3 (ReturnCode: BadParameter)
```

This exception is thrown on a background thread during DDS domain teardown. It propagates as an unhandled exception and kills the test host process mid-suite, aborting all remaining tests.

This is observed consistently after tests that allocate DDS participants (HrotRunnerHarness / CgfHarness). The crash occurs during `_participant.Dispose()` in `HrotRunnerHarness.Dispose()`, specifically when CycloneDDS attempts a `dds_take` on a reader that has already been closed or whose domain participant is being torn down.

The crash is a side effect of Cluster 1: because `SimHostSubsystem.Initialize` throws, the DDS readers/writers created before the throw may be in a partially-initialized state, and disposal of the DDS participant then fails.

### Impact

All tests ordered after the crashing test are not executed. Observed "Total: 36" in an aborted run versus potentially 200+ tests in the full suite.

### Failing Tests (as primary cause of abort)

The crash is triggered by teardown of any test in the `HrotRunnerHarness`-based suite after the navigation-module exception. Specifically observed after:
- `BlueprintKernelRunTests.InstanceBlueprint_TicksInRealKernel_CounterAdvancesByFrameCount(frames: 10)` — takes ~9 seconds and the DDS crash fires during test host teardown.

| Test Cluster | Class | Proposed Fix | SAFE-AUTO-FIX |
|--------------|-------|-------------|----------------|
| All tests aborted after DDS crash | C | Fix Cluster 1 (nav module ordering) to prevent the partial-init state that leaves DDS readers/writers in an inconsistent teardown state. Additionally, add a try-catch around `_participant.Dispose()` in `HrotRunnerHarness.Dispose()` to prevent the crash from aborting the test process. | **NEEDS-DECISION** (root cause fix is in Cluster 1) |

---

## Master Cluster Table

| # | Test / Cluster | Class | Root Cause (file:line) | Proposed Fix | Action |
|---|---------------|-------|----------------------|--------------|--------|
| 1a | SimHostSubsystemTests (15 tests) | **C** | `SimHostNodeBootstrapper.cs:294` — `RegisterProviders` called before `Kernel.Initialize()` | Move `RegisterProviders` call to after `Initialize()`, or merge into `RegisterSystems` | **NEEDS-DECISION** |
| 1b | All HrotRunnerHarness integration tests (≥20 tests) | **C** | Same root cause as 1a | Same | **NEEDS-DECISION** |
| 2 | DataDrivenGizmoPredicateTests (2 tests) | **A** | `DataDrivenGizmoSystem.cs:314` — hard-cast to `DebugPrimitiveBuffer`; test still uses `IDebugDrawBuilder` stub | Replace `D003NoOpDrawBuilder` with `DebugPrimitiveBuffer` in test | **SAFE-AUTO-FIX** |
| 3 | BlueprintKernelRunTests + BlueprintObserveTests.CountEqualsN (8 tests) | **B** | `EditorHarness.cs:152` — `SwitchToDeterministic` burns frames with 200ms BarrierPending lookahead | Inject `TimeConfig { LookaheadWallTicks = 0 }` or call `SwitchToContinuous` before PumpFrames | **SAFE-AUTO-FIX** |
| 4 | BlueprintObserveTests.CaptureLiveState_WithoutDebugMap (1 test) | **A** | `BlueprintDebugSession.cs:1390` — `def.StateFields` fallback now populates fields; test spec says it should be empty | Clarify API contract: update test assertion OR remove StateFields fallback | **NEEDS-DECISION** |
| 5 | CycloneDDS test host crash (aborts remaining suite) | **C** | DDS `dds_take` on disposed reader during HrotRunnerHarness teardown; secondary to Cluster 1 | Fix Cluster 1; add try-catch in `HrotRunnerHarness.Dispose()` | **NEEDS-DECISION** |
