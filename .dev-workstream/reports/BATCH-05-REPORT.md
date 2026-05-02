# BATCH-05 Report

**Batch:** BATCH-05  
**Date:** 2026-02-26  
**Status:** ✅ COMPLETE

---

## Test Results

### `FDP.Toolkit.Perception.Tests` (new)

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| `PerceptionComponentTests` | 5 | 0 | 0 |
| `AudioPerceptionSystemTests` | 3 | 0 | 0 |
| `VisionBroadphaseSystemTests` | 4 | 0 | 0 |
| `ThreatEvaluationSystemTests` | 1 | 0 | 0 |
| `LosRequestBatchingSystemTests` | 2 | 0 | 0 |
| **Total** | **15** | **0** | **0** |

### Regression check — `FDP.Toolkit.Behavior.Tests`

| Project | Passed | Failed |
|---|---|---|
| `FDP.Toolkit.Behavior.Tests` | 25 | 0 |

**`dotnet build FDP.sln` — Build succeeded, 0 errors.**

---

## Task Completion Checklist

### Corrective Fixes (from BATCH-04-REVIEW)

- [x] **Corrective A — HSM magic offset** (`HsmTickSystemTests.cs`)  
  Added `private const int EventXId = 10` and `private const string HsmCurrentEventFieldName = nameof(HsmInstance128.Reserved1)` with a 14-line XML doc comment explaining the layout tie: `HsmKernelCore.CurrentEventId_Offset_128 = 58 == [FieldOffset(58)] of Reserved1`. Injection line now reads `brain.State.Reserved1 = EventXId` with `_ = HsmCurrentEventFieldName` as a documentation anchor.

- [x] **Corrective B — BehaviorIngress assertion gap** (`BehaviorIngressSystemTests.cs`)  
  Added `Assert.Equal(0u, channel.BehaviorInstanceId)` after the existing `Assert.Equal(0, channel.ActiveAction)` in Test 4 (`BehaviorIngress_StaleReplacement_ClearsOldChannel`), documenting the full-reset invariant.

- [x] **Corrective C — Cross-group ordering doc** (`StandardSystemGroups.cs`)  
  Replaced the 3-line `InputSystemGroup` XML doc with a 14-line block covering: registration-order requirement (Input must run before Simulation), cross-group `[UpdateBefore]` limitation in the current scheduler, and a TODO for future attribute support.

### BCS-P2-T1 — Perception Toolkit Foundation

- [x] `FDP.Toolkit.Perception.csproj` created; references `Fdp.Kernel`, `ModuleHost.Core`, `FDP.Toolkit.CarKinem`; `AllowUnsafeBlocks=true`.
- [x] `PerceptionConstants.cs` — `MaxTrackedTargets=4`, `AudioStimulusEventId=4001`, `LosCheckRequestEventId=4002`, `TargetVisibleEventId=4003`, `ThreatScoreDecayPerSecond=0.1f`.
- [x] `Components/PerceptionComponents.cs` — `Faction`, `PerceptionReceptor` (stores precomputed `FieldOfViewCos`), `unsafe struct TargetMemory` with fixed arrays sized via `PerceptionConstants.MaxTrackedTargets` and `static AddOrUpdateTarget(...)` (accumulate → add → evict-lowest → insertion-sort).
- [x] `Events/PerceptionEvents.cs` — `AudioStimulusEvent`, `LosCheckRequestEvent`, `TargetVisibleEvent` each decorated with `[EventId(…)]`.

### BCS-P2-T2 — AudioPerceptionSystem

- [x] `Systems/AudioPerceptionSystem.cs` — `ComponentSystem`, `[UpdateInGroup(SimulationSystemGroup)]`.
  - Consumes `World.Bus.Consume<AudioStimulusEvent>()`.
  - Fast path: `SpatialHashGrid.QueryNeighbors(eventPos2D, evt.Intensity, neighbors)` when `SpatialGridData` singleton exists.
  - Fallback: brute-force `With<SimTransform>().With<PerceptionReceptor>()` scan (used in all tests).
  - Per-candidate: resolves `Entity` via `World.GetEntity(index)`, secondary `HearingRange` check, calls `TargetMemory.AddOrUpdateTarget(..., scoreBoost: 20f)`.
  - Tick source: `World.GlobalVersion` (not `GlobalTime.Tick` — field does not exist on `GlobalTime`; corrected from initial draft during build).

### BCS-P2-T3 — VisionBroadphaseSystem, ThreatEvaluationSystem, PerceptionModule

- [x] `Systems/VisionBroadphaseSystem.cs` — `IModuleSystem`; pure brute-force O(observers × targets).
  - Forward: `Vector3.Transform(Vector3.UnitX, obsTf.Rotation)` — X-east convention (not UnitY, avoids BATCH-01 regression).
  - Filters: self, same faction, distance² > visionRangeSq, dot < FieldOfViewCos.
  - Output: `ecb.PublishEvent(new LosCheckRequestEvent{...})`.
  - Grid optimisation deferred to Phase 3 (see Q2).
- [x] `Systems/ThreatEvaluationSystem.cs` — `IModuleSystem`; read-modify-write via snapshot copy + `ecb.SetComponent`.
  - Step 1 (decay): `score *= 1f − dt × PerceptionConstants.ThreatScoreDecayPerSecond`.
  - Step 2 (boost): consumes `TargetVisibleEvent`, calls `AddOrUpdateTarget(scoreBoost: 50f)`.
- [x] `PerceptionModule.cs` — `IModule`; `Name="Perception"`, `Policy=ExecutionPolicy.SlowBackground(10)`.
  - `GetRequiredComponents()` → `SimTransform, Faction, PerceptionReceptor, TargetMemory`.
  - `Tick()` → `_visionBroadphase.Execute(view, dt); _threatEvaluation.Execute(view, dt)`.

### BCS-P2-T4 — LosRequestBatchingSystem

- [x] `Systems/LosRequestBatchingSystem.cs` — `ComponentSystem`, `[UpdateInGroup(SimulationSystemGroup)]`.
  - Constructor parameter `bool mockMode = false`.
  - Mock (`true`): directly emits `TargetVisibleEvent` for each `LosCheckRequestEvent`.
  - Production (`false`): TODO comment for future `RaycastBatchData` integration.

### Test Project

- [x] `FDP.Toolkit.Perception.Tests.csproj` + `PerceptionTestWorldFactory.cs`.
- [x] 15 tests across 5 suites — all green.
- [x] Both projects added to `FDP.sln` under Toolkits folder `{96325926-29F6-0B84-8A4B-7BABB1BC774A}`.

---

## Q1: How did you handle the missing `PushEvent` API for HSM testing, and how did you tie the test constant to the underlying layout field?

FastHSM has no typed `PushEvent` API accessible from the `EntityRepository` side — the kernel core uses `HsmKernelCore.CurrentEventId_Offset_128 = 58` to read the pending event ID from a raw offset. `HsmInstance128.[FieldOffset(58)] public ushort Reserved1` sits at exactly that offset.

Two named constants were added to `HsmTickSystemTests`:
1. `private const int EventXId = 10` — gives the magic number a name and a natural home.
2. `private const string HsmCurrentEventFieldName = nameof(HsmInstance128.Reserved1)` — documents the field by name rather than offset; if `Reserved1` is ever renamed or relocated the `nameof` expression breaks at compile time, alerting the test author.

The injection line `brain.State.Reserved1 = EventXId` is preceded by `_ = HsmCurrentEventFieldName` (a lint-suppressed discard) so that the field-name constant appears syntactically adjacent to the write, making the architectural tie visible in diffs.

---

## Q2: Why does `VisionBroadphaseSystem` not use `SpatialHashGrid`?

`VisionBroadphaseSystem` is an `IModuleSystem` that receives only an `ISimulationView` — a read-only snapshot. The `SpatialHashGrid` (inside `SpatialGridData`) holds raw entity indices without generation metadata; `QueryNeighbors` returns `(int entityId, Vector2 pos)` tuples. Resolving those indices to valid `Entity` handles via `ISimulationView` requires a method equivalent to `EntityRepository.GetEntity(int index)`, which `ISimulationView` does not expose.

Constructor injection of the grid was explored but rejected: the grid is a live-world object (`SpatialHashSystem` updates it on the main thread) and would create a data-race if read from the async module thread without a locked snapshot copy.

For Phase 2 scale (dozens of entities) brute-force O(observers × targets) is acceptable and avoids the aliasing risk entirely. The production path comment in the source marks the injection point for Phase 3 when a snapshot-safe grid API is available.

---

## Q3: Describe the read-modify-write contract for `TargetMemory` in `ThreatEvaluationSystem`.

The contract enforces three strict phases within a single `Execute(ISimulationView view, float dt)` call:

| Phase | Call | Explanation |
|---|---|---|
| **Read** | `view.GetComponentRO<TargetMemory>(entity)` | Returns a `ref readonly` into the immutable SoD snapshot. No writes via this reference. |
| **Modify** | `TargetMemory mem = memRO;` then local mutation | Copies the value type onto the stack. All score arithmetic is performed on this local copy with no shared state. |
| **Write** | `ecb.SetComponent(entity, mem)` | Enqueues the modified copy in the per-thread `EntityCommandBuffer`. The snapshot is not touched. |
| **Flush** | After `Tick()` returns (kernel side) | The kernel replays all ECBs from all async modules onto the live world on the main thread, serialised before the next Input phase. |

This pattern guarantees that no two async modules can observe each other's mid-frame mutations, preserving snapshot isolation for the duration of the module's execution window.

---

## Q4: Why was `bool mockMode` preferred over a compile-time `#define` for `LosRequestBatchingSystem`?

A `#define` (`#if LOS_MOCK_MODE`) would produce a single code path in the compiled binary. Tests can only exercise one branch per build, requiring separate CI configurations or test assemblies.

A constructor parameter `bool mockMode = false` keeps both code paths in the same binary. The two test cases `new LosRequestBatchingSystem(mockMode: true)` and `new LosRequestBatchingSystem(mockMode: false)` can live side-by-side in `LosRequestBatchingSystemTests.cs`, run in the same test pass, and cover both the "direct visible" and "no-op production" branches without any build-configuration switching. This also makes the intent explicit at the call site — a reviewer immediately sees that mock mode is an opt-in injection rather than an ambient compile flag.
