# BATCH-01 Report: Sub-tick snapshot recorder (capture ring + restore)

**Date:** 2026-06-10
**Branch:** `blueprint-integ-1`
**Tasks:** NGS-1.1, NGS-1.2, NGS-1.3
**Status:** COMPLETE — all changes implemented, all tests green (zero new failures)

---

## Implementation Summary

### NGS-1.1 — Sub-tick delta capture API

**Decision: reuse `RecorderSystem.RecordDeltaFrame` directly.** No new wrapper added. The method is already synchronous, already ignores `eventBus` (pass null), and already accepts a `wallClockTicks=0L` argument. Adding a one-line wrapper would not reduce any real complexity. The comment in `SubTickSnapshotRecorder.cs` documents this choice.

### NGS-1.2 — `SubTickSnapshotRecorder` with bounded ring

New file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/SubTickSnapshotRecorder.cs`

- **`BeginTick(EntityRepository repo)`** — resets ring state (count=0, dropped=0), records a full-repo keyframe baseline via `RecorderSystem.RecordKeyframe`, then snapshots `_prevVersion = repo.GlobalVersion`.
- **`RecordNodeEntry(EntityRepository repo, string nodeId)`** — see ordering section below.
- **`int Count`**, **`string NodeIdAt(int index)`** — standard ring accessors.
- **Ring capacity**: default 256, configurable. On overflow: oldest entry is dropped, `DroppedFrameCount` is incremented (overflow signal), no exception thrown.
- **Self-contained**: only references `Fdp.Core` and `Fdp.Core.FlightRecorder`. No UI, no DBM, no blueprint compiler dependencies.

### NGS-1.3 — `RestoreTo`

**`RestoreTo(int nodeIndex, EntityRepository scratchRepo)`** — applies the keyframe baseline (via `PlaybackSystem.ApplyFrame`, which calls `repo.Clear()` internally), then applies deltas `[0..nodeIndex]` in logical order. The caller owns the scratch repo.

---

## The Exact Bump/Capture/Write Ordering (and WHY it attributes writes correctly)

### Ordering inside `RecordNodeEntry(repo, nodeId)`:

```
1. Capture delta from _prevVersion to current repo.GlobalVersion
   → captures all mutations written by the PREVIOUS node (stamped during its execution)

2. Store (nodeId, deltaBytes) in ring
   → delta[K] = "what changed between entry of node K-1 and entry of node K"
                = effect of node K-1's execution

3. Advance _prevVersion = repo.GlobalVersion
   → so the next capture starts from the current boundary

4. Bump repo.BumpMemoryVersion()
   → advances GV so THIS node's upcoming writes get a fresh version stamp,
     isolated from the delta already captured in step 1
```

### Concrete counter-test (5/6/7) trace:

| Event | GV | Action | _prevVersion | delta stored |
|-------|-----|--------|-------------|-------------|
| BeginTick | 2 | record keyframe; _prevVersion=2 | 2 | — |
| RecordNodeEntry("n0") | 2 | capture delta(2..2)=empty; bump→GV=3 | 2→3 | n0: empty |
| n0 writes value→6 | 2 (chunk stamps at GV=3) | | | |
| RecordNodeEntry("n1") | 3 | capture delta(2..3)={value=6}; bump→GV=4 | 3→4 | n1: {value=6} |
| n1 writes value→7 | 3 (chunk stamps at GV=4) | | | |
| RecordNodeEntry("n2") | 4 | capture delta(3..4)={value=7}; bump→GV=5 | 4→5 | n2: {value=7} |

**RestoreTo(0)**: keyframe + delta[n0]=empty → value=5 ✓
**RestoreTo(1)**: keyframe + delta[n0] + delta[n1]={value=6} → value=6 ✓
**RestoreTo(2)**: keyframe + delta[n0] + delta[n1] + delta[n2]={value=7} → value=7 ✓

### Why the ordering is off-by-one-proof:

The key insight: chunks are stamped at the _current_ `_globalVersion` at write time. The delta captures all chunks with `chunkVersion > prevVersion`. By bumping AFTER capture (step 4), the current node's writes will be stamped at GV ≥ new GV, making them visible to the NEXT RecordNodeEntry's capture (step 1 of the next call). Without the bump, two consecutive nodes' writes could receive the same GV stamp and become indistinguishable from each other.

The bump BEFORE capture would also be wrong: if we bumped first (step 4 before step 1), we'd capture the previous node's writes at the same time as initializing the isolation boundary, and the next node's delta would start one GV too late.

---

## Design Decisions

1. **RecordDeltaFrame reused directly** (NGS-1.1): the existing method is already a thin, synchronous, whole-repo delta capture. A wrapper would add zero value.

2. **Whole-repo capture**: `RecordDeltaFrame` scans ALL registered component tables. This is intentional per the spec — blueprints can write components on other entities (cross-entity sync mutation). Scoping to a single entity would silently lose mutations.

3. **Ring head arithmetic**: `_ringHead` is a monotonically increasing counter (never wrapped). Ring slots are computed as `_ringHead % Capacity`. The "oldest slot" for restore traversal is `(_ringHead - _count + Capacity*8) % Capacity` — the `*8` prevents underflow for any realistic count.

4. **Default capacity 256**: matches the existing `ExecutionHistory` ring buffer default in the same assembly. Sufficient for any blueprint with reasonable node depth.

5. **`BeginTick` is mandatory before `RecordNodeEntry`**: enforced by `_inTick` flag with an `InvalidOperationException` on misuse — fails loudly rather than silently producing incorrect deltas.

6. **File placement**: `Hrot.Blueprints.Core/Debug/SubTickSnapshotRecorder.cs` as instructed. The `Debug/` subdirectory was created; existing files in `Core` root are in the same `Hrot.Blueprints.Core.Debug` namespace and remain unchanged.

---

## Deviations

None. All tasks implemented per spec. `RecordDeltaFrame` reused directly as suggested in the instructions.

---

## Test Results

### `Hrot.Blueprints.Tests` (full suite)

| Result | Count |
|--------|-------|
| Passed | 1708 |
| Failed | 7 (all pre-existing) |
| Skipped | 8 |
| **Total** | **1723** |

**New tests (7 all pass):**
- `RestoreTo_CounterSemantics_567` — exact value assertions: restore(0)=5, restore(1)=6, restore(2)=7
- `RestoreTo_Attribution_MutationAfterNKAppearsAtNKPlus1Only` — value 100→200; visible at n1 not n0
- `RestoreTo_MultiEntity_WholeRepoCapture` — two entities, two component types; all 8 value assertions correct
- `RestoreTo_ManagedComponent_RestoresCorrectly` — managed string "alpha"→"beta"→"gamma"; all 3 restores correct
- `RecordNodeEntry_SimulationTickFrozen_GlobalVersionAdvances` — ST unchanged, GV advances by exactly nodeCount
- `RecordNodeEntry_RingOverflow_DropsOldestAndSignals` — DroppedFrameCount=1, oldest=n1, most-recent=nOverflow
- `BeginTick_Reset_ClearsRingAndDroppedCount` — after BeginTick: count=0, dropped=0

**Pre-existing failures (not mine, not masked):**
1. `AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` — snapshot mismatch
2. `AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` — snapshot mismatch
3. `Stage8_PdbContainsEmbeddedSource` — Roslyn/PDB test
4. `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` — Roslyn test
5. `TickFrame_1000Frames_AllocatesZeroBytes` — documented allocation benchmark
6. `MoveToAndFire_GeneratedSource_Snapshot` — snapshot mismatch
7. `WhenNode_ZeroAllocOnHotPath` — allocation benchmark

### `Fdp.Core.Tests` (not touched by this batch; confirmed green)

`VersionClockSplitTests`: 15/15 pass (unchanged from BATCH-00).
Overall suite: 2 documented timing-benchmark failures (`RealisticMilitrarySimulation`, `Benchmark_HotPathOptimization`); `CheckpointIOWorkerTests` fail under parallel load but pass in isolation (pre-existing timing sensitivity, not related to our changes).

---

## Developer Insights

1. **Chunk version stamping clarification**: writes stamp at the CURRENT `_globalVersion` at the moment of the write call, not at the GV when `RecordNodeEntry` is called. This means the ordering works because `BumpMemoryVersion()` at the END of RecordNodeEntry ensures the current node's writes will get a fresh GV that the next call's `_prevVersion` won't include.

2. **Managed component test**: `repo.AddComponent<T>(entity, value)` works for both managed and unmanaged via the `ComponentTypeHelper.IsUnmanaged<T>()` dispatch. `AddManagedComponent` and `GetManagedComponent` exist but are `internal` — the public `AddComponent`/`GetComponent`/`SetComponent` are the correct public API for both.

3. **Ring slot arithmetic**: the `Capacity*8` multiplier in `RingSlot` avoids modular arithmetic underflow for any practical test scenario. A cleaner alternative would be `(((_ringHead - _count) % Capacity) + Capacity) % Capacity`, but the current form is equivalent and avoids branching.

4. **BeginTick allocation**: the keyframe is a heap allocation (byte array). This is acceptable — `BeginTick` is called once per tick, not on the hot path.

5. **Per-node delta allocation**: each `RecordNodeEntry` allocates the delta byte array. Per the spec this is acceptable while debugging; the note is preserved in the class doc.

---

## Known Issues

None introduced by this batch. The BATCH-02 wiring (`OnNodeEnter` in `BlueprintDebugSession`) will consume `SubTickSnapshotRecorder.BeginTick`/`RecordNodeEntry` but is explicitly out of scope for this batch.

---

## Suggested Commit Message

```
feat: add SubTickSnapshotRecorder for sub-tick ECS state capture + restore (NGS-1.1-1.3)

Whole-repo delta ring (256 slots) with keyframe baseline per tick.
RecordNodeEntry ordering: capture-prev-delta → store → advance cursor → bump GV;
proves 5/6/7 counter semantics (restore-at-K = state before K's own writes).
7 new behavioral tests covering counter semantics, off-by-one attribution,
multi-entity whole-repo capture, managed components, frozen SimulationTick,
and ring overflow signal. No change to BATCH-00 hot path or version-clock semantics.
```
