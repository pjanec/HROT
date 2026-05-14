# BATCH-02 Report

**Status:** COMPLETE — all tasks implemented, built, and tested.

---

## SM-003: StrideNodeBootstrapper

### New File

`Hrot/Subsystems/Hrot.StrideMock/StrideNodeBootstrapper.cs`

- `public sealed class StrideNodeBootstrapper : SharedApplicationBootstrapper, IDisposable`
- Implements all 6 abstract hooks from `SharedApplicationBootstrapper`.
- Public static `Role = NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver | NodeRole.ImageGenerator`.
- Public properties: `Context`, `SimGroup`, `PostSimGroup`, `ProducerBuffer`, `ConsumerBuffer`, `Camera`.
- `Tick(float dt)` — forwards `dt` to `Context.Kernel.Update(dt)` (legacy overload, pragma-suppressed) because `SlaveSyncController` produces zero delta in headless/offline mode where no network sync events arrive.
- `Dispose()` — disposes `Context?.Participant`.
- `RegisterDomainComponents` — registers `HrotSharedComponentRegistry`, `KinematicComponentRegistry`, visual effect components, combat notification events, and genesis intent DTOs.
- `PopulateSystems` — adds `EventToEffectSystem` to sim list, `VisualEffectCleanupSystem` to postSim list (SM-005).
- `BuildOrchestration` — saves `SimGroup`/`PostSimGroup`, delegates to `_nodeBootstrapper.BuildOrchestration`.
- `RegisterSpawningPipeline` — registers `GenesisMaterializationSystem`; conditionally wires `NetworkSpawningSystem` when `IdAllocator != null`.
- `RegisterNetworkTranslators` — registers `SimHostAuxiliaryTranslators`.

### Tests

`Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.Tests/StrideNodeBootstrapperTests.cs`

12 test cases (SC_SM003_1 through SC_SM003_11, including SM-005 tests SC_SM005_1 and SC_SM005_2):

| Test ID | Description | Result |
|---|---|---|
| SC_SM003_1 | `BootstrapNode` does not throw with headless factory | PASS |
| SC_SM003_2 | `Context.ClusterSlave` is non-null after bootstrap | PASS |
| SC_SM003_3 | `ProducerBuffer` and `ConsumerBuffer` are separate instances | PASS |
| SC_SM003_4 | `Camera` is non-null with default zoom | PASS |
| SC_SM003_5 | `TimeControl` accessible via inherited base property | PASS |
| SC_SM003_6 | `VisualEffectState` registered in world | PASS |
| SC_SM003_7 | `TracerTarget` registered in world | PASS |
| SC_SM003_8 | `Tick` can be called repeatedly without throwing | PASS |
| SC_SM003_9 | Kinematic components registered in world | PASS |
| SC_SM003_10 | Cognitive components (`BrainHsm128`) NOT registered | PASS |
| SC_SM003_11 | Togglable groups contain correct systems for replay safety | PASS |
| SC_SM005_1 | `EventToEffectSystem` in sim group; `VisualEffectCleanupSystem` in postSim group | PASS |
| SC_SM005_2 | Same as SC_SM003_11 (togglable group structure verified) | PASS |

---

## SM-004: SyncFdpToStrideScript + Tests

### Pre-existing File

`Hrot/Subsystems/Hrot.StrideMock/SyncFdpToStrideScript.cs` — created in a prior session.

**Fix applied this session:** Added `using Fdp.ModuleHost.Abstractions;` to resolve `ISimulationView` (used in cast on lines 92 and 124 in `SyncStrideEntities` and `SyncStrideEffects`).

### Tests

`Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.Tests/SyncFdpToStrideScriptTests.cs`

8 test cases (SC_SM004_1 through SC_SM004_8):

| Test ID | Description | Result |
|---|---|---|
| SC_SM004_1 | Spawned entity with SimTransform appears in `ActiveEntities` | PASS |
| SC_SM004_2 | Loading state — `SyncStrideEntities` not called; splash message non-empty | PASS |
| SC_SM004_3 | Returning to Operating state resumes sync; splash message empty | PASS |
| SC_SM004_4 | Destroyed entity removed from `ActiveEntities` on next `Update` | PASS |
| SC_SM004_5 | Recycled entity (generational safety): old entry removed, new entry created | PASS |
| SC_SM004_6 | `WeaponFireNotification` results in `FakeStrideEffect` in `ActiveEffects` | PASS |
| SC_SM004_7 | Expired effect removed from `ActiveEffects` after cleanup flush | PASS |
| SC_SM004_8 | `_staleEntities` list reused across frames (no per-frame GC alloc) | PASS |

**Key timing discovery (SC_SM004_7):** `VisualEffectCleanupSystem` runs as a global PostSimulation system
(registered via `RegisterGlobalSystem`). Its command buffer is flushed at the start of the NEXT tick's
BeforeSync phase, not immediately after execution. Module systems (registered via `RegisterModule`) have
their buffers flushed by `PlaybackCommands` immediately after each module tick. The test therefore requires
one additional `boot.Tick(0f)` after the expiry tick to flush the `DestroyEntity` command before calling
`script.Update(0f)`.

---

## SM-005: Visual Effects Wiring

Implemented inside `SM-003` (no separate files):

- `PopulateSystems` adds `new EventToEffectSystem()` to the `sim` list → runs in `TogglableSimulationGroup` (suspendable during replay).
- `PopulateSystems` adds `new VisualEffectCleanupSystem()` to the `postSim` list → runs in `TogglablePostSimulationGroup` (suspendable during replay).
- Tests SC_SM005_1 and SC_SM005_2 verify system placement.

---

## Bug Fixes Applied This Session

| Bug | Root Cause | Fix |
|---|---|---|
| `ISimulationView` not found in `SyncFdpToStrideScript.cs` | Missing `using Fdp.ModuleHost.Abstractions;` | Added the using directive |
| `NodeRole` not found in `StrideNodeBootstrapper.cs` | Missing `using Hrot.Common;` | Added in previous session |
| `Kernel.Update()` ignores `dt` in headless mode | `SlaveSyncController` uses real wall clock with no network events | Changed to `Kernel.Update(dt)` with `#pragma warning disable CS0618` |
| SC_SM004_7 fails — effect still in `ActiveEffects` after expiry | PostSimulation global system commands deferred to next tick | Added one extra `boot.Tick(0f)` to flush the `DestroyEntity` command |

---

## Build Summary

```
Hrot.StrideMock:       Build succeeded  0 errors
Hrot.StrideMock.Tests: Build succeeded  0 errors
```

---

## Test Summary

```
Total tests: 30
     Passed: 30
     Failed: 0
Total time:  ~1.4 seconds
```

Test classes:
- `SharedApplicationBootstrapperTests` — 10 tests (BATCH-01, unchanged)
- `StrideNodeBootstrapperTests`        — 12 tests (BATCH-02 SM-003 + SM-005)
- `SyncFdpToStrideScriptTests`         —  8 tests (BATCH-02 SM-004)
