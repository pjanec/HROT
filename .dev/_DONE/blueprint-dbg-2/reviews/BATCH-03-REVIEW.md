# BATCH-03 Review
**Status:** ✅ APPROVED (feature works; raises an architectural decision for the user — see Finding)   **Date:** 2026-06-10

## Summary
Virtual-pointer navigation (Step/StepBack while paused, clock untouched) + inspector redirect to the pointer's restored state. CT0a (entity-scope) and CT0b (exact assertion) done. The headline feature works: a single paused tick shows different, correct per-node values.

## Verification performed (independent)
- **Zero regressions — definitively confirmed.** Stashed all BATCH-03 changes → clean baseline (`7b1aae5b`) → ran full `Hrot.Blueprints.Tests`: identical 7 failing test names (`AiPrimitive_EmitMatchesGoldenSource`×2, `Stage8_*`×2, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_*Snapshot`, `WhenNode_ZeroAllocOnHotPath`) and the same "8" summary artifact. Current tree = baseline failures + 5 new passing tests (1711→1716 passed, 8→8 failed, +5 total). All 7 reds are compiler/codegen/PDB/alloc — unrelated to recording code.
- Read all tests. `Inspector_ReturnsExactPerNodeValues` asserts the EXACT sequence A=0 (node0), 0 (node1), 10 (node2), 0 (StepBack to node1) — proves per-node state differs within one paused tick + StepBack. Pointer clamps both ends, cleared on Continue, inspector null after Continue. CT0a: two entities, filter to A, breakpoint on A → only A's nodes recorded, restored A=10 (A's logic, B excluded). CF-6 fallback: no live repo → StepInto sets temp BPs + resumes + re-pauses next tick.
- Ran new tests on restored tree → **9/9 pass**.
- Entity-scope guard `IsRecordingEntity`: `_recordingEntity` exact → else `_entityFilter` → else any. Sound when scoped (the debugger sets a filter).

## Finding (architectural — needs user decision, NOT a blocker)
**The recorder pivoted from per-node DELTA capture to full-KEYFRAME-per-node.** Reason (empirically proven by tests flipping fail→pass): blueprint `SetVar` writes go directly into the blackboard memory span, bypassing `GetComponentRW`, so chunk versions don't advance per-node and delta detection (`HasChunkChanged`) misses them. Keyframe-per-node captures the actual bytes regardless of versions → correct.

**Consequence:** BATCH-00's semantic split (`_simulationTick` vs `_globalVersion`, `BumpMemoryVersion`, frame-clock consumer migrations) was built to enable per-node deltas. With keyframes, it is **largely vestigial** — `RecordNodeEntry` still calls `BumpMemoryVersion()` but only to satisfy a test; it serves no functional purpose for the keyframe recorder. Also reintroduces the full-keyframe-per-node cost the architect flagged (mitigated: debug-active-only, unmanaged keyframe path is zero-alloc; managed chunks alloc).

**User decision (morning):** (a) revert BATCH-00 to simplify (drop the unused split + migrations + the pointless bump), accepting keyframe-per-node as the design; or (b) keep BATCH-00 (harmless when no bumps, since SimulationTick==GlobalVersion). Recommend (a) for simplicity unless the split is wanted for a future delta path.

## Residual items (debt)
- P3: `IsRecordingEntity` still records all entities when NEITHER `_recordingEntity` NOR `_entityFilter` is set (narrow; debugger normally sets a filter).
- Keyframe-per-node cost on very large worlds (debt; revisit if needed).

## Verdict
APPROVED — committed. Loop paused for user input on the BATCH-00 vestigiality decision before BATCH-04 (step-past-end tick-bridge) and the cost question.
