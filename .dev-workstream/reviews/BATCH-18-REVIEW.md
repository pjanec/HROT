# BATCH-18 Review

**Batch:** BATCH-18  
**Reviewer:** Development Lead  
**Date:** 2026-02-25  
**Status:** ✅ APPROVED — clean, no issues found

---

## Summary

All tasks complete. DEBT-027 is fully resolved with the correct architectural approach — full
`Entity` handles (Index + Generation) flow through all seven stages of the LOS pipeline. No
`GetEntityByIndex` anywhere. 34/34 tests pass. Three documentation tasks done. All tracked
technical debt is now resolved.

---

## DEBT-027 — LOS Pipeline Generational Safety ✅

### Root cause thoroughly traced

The developer correctly identified that the raw-int smell originated at `VisionBroadphaseSystem`
line 122 — not at `HitResolutionSystem` where the DEBT comment lived. The full flow was traced
across all seven stages:

1. `VisionBroadphaseSystem` — emit site ✅ (now `Observer = observer, Target = target`)
2. `LosCheckRequestEvent` — struct fields ✅ (`Entity Observer/Target`)
3. `LosRequestBatchingSystem` mock path ✅ (pass-through, no re-packing)
4. `RaycastRequest` ✅ (`Entity Observer/Target` fields added)
5. `RaycastSolverSystem` ✅ (propagates `Observer`/`Target` to hit struct)
6. `RaycastHit` ✅ (`Entity Observer/Target` fields)
7. `HitResolutionSystem` ✅ (reads `hit.Observer`/`hit.Target`; bit-unpacking for entity identity eliminated; DEBT comment removed)
8. `TargetVisibleEvent` ✅ (`Entity Observer/Target`)
9. `ThreatEvaluationSystem` ✅ (`IsAlive` guards before any component access)

`RayId` bit-unpacking (`>> 32`, `& 0xFFFF_FFFF`) is gone from entity identity recovery. `RayId`
is still used only for `IsBulletRay` discrimination — the correct residual use.

### Three notable quality decisions

**1. `RayId` legacy payload retained — correct.**
`PackLosRayId(int, int)` still packs raw indices into the long's payload, but that payload is
now never read for identity. The decision to leave it as-is is correct: cleaning up
`PackLosRayId` API would have no functional effect and risks touching unrelated bullet-path code.
Deferred cleanup is explicitly documented.

**2. `target.PackedValue` as `TargetMemory` key — correct and improves correctness.**
This is an upgrade, not a deviation. Previously `(long)target.Index` meant a recycled entity
at the same index would silently collide with an existing TargetMemory slot. Using `PackedValue`
(Index + Generation encoded into one long) means a recycled slot gets a different key —
old entries for destroyed entities are orphaned (pending future eviction) rather than falsely
matched. The pre-existing Test 2 was correctly updated to use `(long)target.PackedValue` in
both seed and assertion.

**3. `ThreatEvaluationSystem` loop elimination — bonus improvement.**
The previous implementation searched for target entity by iterating entities to find a position.
With full `Entity` handles, `view.GetComponentRO<SimTransform>(evt.Target)` is a direct O(1)
access. Simpler code, better performance. No separate tracking needed.

### `AudioStimulusEvent` — Q1 decision accepted

The decision to leave `AudioStimulusEvent.SourceEntityIndex : int` unchanged is reasonable and
well-argued. The audio consumer uses it only as an opaque long key (no component access on the
source entity), and the same-frame publish/consume model makes the recycling window very narrow.
The report flags it for a future debt item. Accepted as-is.

---

## Tests ✅

**34/34 pass (30 pre-existing + 4 new).**

Test 4 — `ThreatEvaluationSystem_SkipsEvent_WhenObserverRecycled`:  
Destroys observer *before* consume. `IsAlive(observer)` → false. No crash. ✅

Test 5 — `ThreatEvaluationSystem_SkipsEvent_WhenTargetRecycled`:  
Destroys target before consume. `resultMem.Count == 0` — no boost applied. ✅  
This assertion is stronger than "no crash" — it verifies the guard is actually preventing the write.

Test 6 — `ThreatEvaluationSystem_UpdatesThreatMemory_WhenBothAlive`:  
Positive path. `Count == 1`, `ThreatScores[0] >= 50`, `EntityIds[0] == (long)target.PackedValue`. Three assertions covering score, count, and key correctness. ✅

Test 7 — `LosCheckRequestEvent_CarriesFullEntityHandle_NotRawIndex`:  
Runs the actual `VisionBroadphaseSystem` and reads the emitted `LosCheckRequestEvent` off the bus.
Asserts `events[0].Observer == observer` (full Entity equality, not just index), and
`events[0].Observer.Generation != 0` (proves a live, non-null entity, not a raw index zero-padded into an Entity struct). This is a high-value integration test — it pins the contract at the pipeline entry point. ✅

---

## Documentation Tasks ✅

- **DEV-GUIDE.md:** HSM action registration pitfall (Pitfall 9) added — `[HsmAction]`, `Fhsm.SourceGen`, `RegisterAll()`, case-sensitive FNV-1a hash warning. ✅
- **DEBT-007-HSM-ANALYSIS.md:** Status header updated to ✅ FULLY RESOLVED in BATCH-17. ✅
- **TASK-TRACKER.md:** Project closure note added. ✅

---

## Commit Message (approved)

```
fix(BATCH-18): resolve DEBT-027 — full Entity handles through LOS pipeline + docs

DEBT-027: Replace raw int indices with full Entity handles (Index + Generation)
  across all 7 stages of the LOS pipeline:
  - PerceptionEvents.cs: LosCheckRequestEvent, TargetVisibleEvent → Entity Observer/Target
  - VisionBroadphaseSystem: emit Observer=observer, Target=target (no .Index stripping)
  - LosRequestBatchingSystem: mock path passes Entity handles straight through
    (production stub comment updated: no raw index re-packing in future impl)
  - PhysicsComponents.cs: RaycastRequest + RaycastHit → Entity Observer/Target fields added
  - RaycastSolverSystem: propagates Observer/Target to hit struct
  - HitResolutionSystem: reads hit.Observer/hit.Target; RayId bit-unpacking for entity
    identity eliminated; DEBT-027 comment removed
  - ThreatEvaluationSystem: IsAlive guards; PackedValue used as TargetMemory key;
    direct GetComponentRO<SimTransform>(evt.Target) replaces entity search loop
  +4 tests: recycled observer guard, recycled target guard, happy path, entity handle contract

Docs:
  - DEV-GUIDE.md: Pitfall 9 — HSM action registration (discovered BATCH-17)
  - DEBT-007-HSM-ANALYSIS.md: status updated to RESOLVED
  - TASK-TRACKER.md: project closure note added

Tests: 34 / 34 pass. Zero build errors. ALL TRACKED DEBT RESOLVED.
```

---

## Project Status

With BATCH-18 approved, the Behavior Control Subsystem project is complete:

| | |
|---|---|
| **Tasks** | 43/43 ✅ (BATCH-01 — BATCH-17) |
| **Debt items** | 38/38 ✅ (DEBT-001 — DEBT-038; all resolved) |
| **Tests** | 34/34 passing |
| **Open issues** | None |
