# BATCH-18 Report: Project Finalization — DEBT-027 Resolution + Documentation Hardening

**Batch:** BATCH-18  
**Date:** 2026-02-25  
**Status:** ✅ COMPLETE  

---

## Summary

All BATCH-18 tasks completed:
- **DEBT-027** resolved: Full `Entity` handles flow through the entire LOS pipeline.
- **DEV-GUIDE.md** updated: HSM action registration pitfall documented.
- **DEBT-007-HSM-ANALYSIS.md** created with FULLY RESOLVED status header.
- **TASK-TRACKER.md** updated: Project closure note added.
- All 34 tests pass (30 pre-existing + 4 new).

---

## DEBT-027 — Pipeline Changes

### Files Modified

| File | Change |
|---|---|
| `FDP.Toolkit.Perception/Events/PerceptionEvents.cs` | `LosCheckRequestEvent`: `int` fields → `Entity Observer/Target`. `TargetVisibleEvent`: same. |
| `FDP.Toolkit.Perception/Systems/VisionBroadphaseSystem.cs` | Emit site: `Observer = observer, Target = target` |
| `FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs` | Mock path: pass Entity handles through. Production stub comment updated. |
| `FDP.Toolkit.Physics/Components/PhysicsComponents.cs` | `RaycastRequest`: added `Entity Observer/Target`. `RaycastHit`: same. |
| `FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` | Hit construction: `Observer = req.Observer, Target = req.Target` |
| `FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` | Removed bit-unpacking. `TargetVisibleEvent`: `Observer = hit.Observer, Target = hit.Target`. DEBT-027 comment removed. |
| `FDP.Toolkit.Perception/Systems/ThreatEvaluationSystem.cs` | `IsAlive` guards. Direct entity access (no loops). `entityId: (long)evt.Target.PackedValue`. |

### Tests Updated (Step H)

- `VisionBroadphaseSystemTests.cs` — 2 assertion sites: `Index` comparisons → `Entity` equality.
- `LosRequestBatchingSystemTests.cs` — Raw int literals → `new Entity(n, 1)` construction.
- `ThreatEvaluationSystemTests.cs` — Seed entityId: `(long)target.Index` → `(long)target.PackedValue`. Publish: `ObserverEntityIndex/TargetEntityIndex` → `Observer/Target`.
- `HitResolutionSystemTests.cs` — Test 1: added `Observer`/`Target` fields to `RaycastHit`; assertions use `Entity` equality.

### New Tests (Step I)

1. `ThreatEvaluationSystem_SkipsEvent_WhenObserverRecycled` — IsAlive guard skips stale event when observer destroyed.
2. `ThreatEvaluationSystem_SkipsEvent_WhenTargetRecycled` — IsAlive guard skips stale event when target destroyed; TargetMemory.Count == 0.
3. `ThreatEvaluationSystem_UpdatesThreatMemory_WhenBothAlive` — Happy path: score boosted, EntityId uses packed value.
4. `LosCheckRequestEvent_CarriesFullEntityHandle_NotRawIndex` — VisionBroadphaseSystem emits full Entity (generation non-zero).

---

## Q1: AudioStimulusEvent.SourceEntityIndex — Recycling Risk?

**Decision: Left as-is. Documented below.**

`AudioStimulusEvent.SourceEntityIndex : int` has a *theoretical* recycling risk, but it is lower severity than the LOS pipeline for two reasons:

1. **No component access on the source entity.** `AudioPerceptionSystem` uses `SourceEntityIndex` only as an opaque `long` key in `TargetMemory.AddOrUpdateTarget`. If the index is recycled, the worst outcome is that a TargetMemory slot has its `EntityId` match a different (new) entity — a stale score entry, not a crash or incorrect component mutation on a live entity.

2. **Same-frame publish and consume.** `AudioPerceptionSystem` is a `ComponentSystem` in `SimulationSystemGroup`. `AudioStimulusEvent` is published by other systems in the same group earlier in the frame. The window for recycling within one frame is extremely narrow (entity destruction typically happens at frame boundaries via ECB playback, not mid-group).

A future DEBT item could carry a full `Entity` in `AudioStimulusEvent` for strict consistency. This is not done in BATCH-18 to keep scope focused on the LOS pipeline (DEBT-027).

---

## Q2: RayId Still Encodes Raw Indices in PackLosRayId

**Decision: Kept as-is (legacy, now unused for entity recovery).**

`PhysicsConstants.PackLosRayId(int observerIdx, int targetIdx)` still packs raw indices into the `long RayId` field. This encoding is retained for backward compatibility and because:

1. `RayId` is still needed to discriminate bullet rays from LOS rays (`IsBulletRay` checks bit 63). The `RayId` field layout must remain stable for the bullet path.
2. The raw-index payload in `PackLosRayId` is now **never read** for entity identity — `HitResolutionSystem` reads `hit.Observer`/`hit.Target` instead.
3. Cleaning up `PackLosRayId` to stop packing raw indices is a separate refactor with no functional benefit at this stage (it would only simplify the API). Deferred to a future housekeeping batch.

The `RayId` in LOS hits now carries redundant/legacy index data in its payload. This is acceptable: the field is not read for identity purposes, and the `IsBulletRay` discrimination remains correct.

---

## Q3: Other Consumers of TargetVisibleEvent

Searched the full codebase. **`ThreatEvaluationSystem` is the only consumer** of `TargetVisibleEvent`. No other systems subscribe to this event.

Evidence: `grep_search` for `ConsumeEvents<TargetVisibleEvent>` and `Consume<TargetVisibleEvent>` returned only:
- `ThreatEvaluationSystem.cs` (production consumer)
- Test files (HitResolutionSystemTests, ThreatEvaluationSystemTests) — consume for assertion only.

---

## Q4: LosRequestBatchingSystem Production Stub Updated

Yes. The production-mode commented-out stub was updated to reference `req.Observer` and `req.Target`:

```csharp
// Production mode (Phase 3+): batch rays into RaycastBatchData.
// TODO: Add to RaycastBatchData.Requests when the Physics toolkit is available.
// Use req.Observer and req.Target (full Entity handles) — do NOT re-pack as raw indices.
// foreach (ref readonly var req in requests)
// {
//     var raycastBatch = ref World.GetSingleton<RaycastBatchData>();
//     raycastBatch.AddRequest(req.Observer, req.Target);
// }
```

---

## Q5: Surprises in Pipeline Structure

**ThreatEvaluationSystem's nested loops eliminated.** The previous implementation found the observer and target by iterating all entities twice (a linear scan per event). With full `Entity` handles, both lookups become direct component access via the entity key — a significant simplification and a performance improvement proportional to entity count.

**Entity.PackedValue encoding for TargetMemory keys.** `TargetMemory.EntityIds` stores `long` values. Previously `(long)target.Index` was used. After the fix, `(long)target.PackedValue` encodes both index and generation into the key. This is a breaking change to the semantic meaning of the key (generation now included), which required updating the existing `ThreatEvaluation_BoostsScore_OnTargetVisibleEvent` test to seed with `(long)target.PackedValue` instead of `(long)target.Index`.

**`LayoutKind.Sequential` and new Entity fields.** Both `RaycastRequest` and `RaycastHit` use `[StructLayout(LayoutKind.Sequential)]`. Adding `Entity` fields (which are also `[StructLayout(LayoutKind.Sequential)]` with `int Index` + `ushort Generation`) is valid and unmanaged — no NativeArray compatibility issues.

---

## Test Results

```
Total: 34 passed, 0 failed
```

Pre-existing tests: 30 (all green).  
New BATCH-18 tests: 4 (all green).

---

## Documentation Changes

- **DEV-GUIDE.md:** Section 9 "HSM Action Delegates — Registration Is Not Automatic" added under Common Pitfalls.
- **DEBT-007-HSM-ANALYSIS.md:** Created with STATUS: ✅ FULLY RESOLVED in BATCH-17 header and full architectural reference.
- **TASK-TRACKER.md:** Project closure note added after summary table.
