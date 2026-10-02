# BATCH-01: Sub-tick snapshot recorder (capture ring + restore)

**Tasks:** NGS-1.1, NGS-1.2, NGS-1.3   **Phase:** Recorder mechanism   **Est:** ~12h
**Dependencies:** BATCH-00 (committed `040f6f82` — `BumpMemoryVersion()`, `SimulationTick`).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — how you work.
2. `.dev/_DONE/blueprint-dbg-2/PLAN.md` — feature + design.
3. `.dev/_DONE/blueprint-dbg-2/reviews/BATCH-00-REVIEW.md` and `reports/BATCH-00-REPORT.md` — what's already in place.
4. This file.

## Scope of THIS batch (read carefully)
Build a **self-contained** sub-tick snapshot recorder, tested directly against a real `EntityRepository` — **NO** blueprint compilation, **NO** `BlueprintDebugSession` wiring, **NO** DBM in this batch. Wiring into `OnNodeEnter` and the virtual-pointer navigation is BATCH-02. This batch proves the capture+restore mechanism in isolation with hard runtime-value assertions.

## Key facts already verified (BATCH-00)
- `EntityRepository.BumpMemoryVersion()` advances ONLY `_globalVersion` (frame clock `SimulationTick` frozen).
- Chunk versions are stamped from `_globalVersion` on write (`NativeChunkTable.GetRefRW`), diffed RELATIVELY: `HasChunkChanged` uses `chunkVersion > prevTick`.
- `RecorderSystem.RecordKeyframe(repo, writer, wallClockTicks, eventBus=null, serializeReadBuffer=false)` and `RecorderSystem.RecordDeltaFrame(repo, prevTick, writer, wallClockTicks, eventBus=null, serializeReadBuffer=false)` are **synchronous** and write to a caller-owned `BinaryWriter` (BATCH-00's round-trip test exercises both directly).
- `PlaybackSystem.ApplyFrame(repo, BinaryReader)` applies a keyframe (clears repo first) or a delta (overwrites changed chunks) — BATCH-00 proved keyframe+delta replay reconstructs exact component values.
- These live in `FDP/Engine/Fdp.Core/FlightRecorder/`. `EntityRepository` in `FDP/Engine/Fdp.Core/`.

## Tasks — complete IN ORDER; do not start the next until prior code + tests are green.

### Task 1: Sub-tick delta capture API (NGS-1.1)
Decide and document: reuse `RecorderSystem.RecordDeltaFrame` directly for whole-repo sub-tick capture (preferred — it already does exactly this synchronously), OR add a thin convenience method `RecordSubTickDelta(EntityRepository repo, uint prevVersion, BinaryWriter writer)` if it adds real clarity (e.g. always `eventBus=null`, `wallClockTicks=0`). Do NOT use `AsyncRecorder`. If you add a method, put it on `RecorderSystem` and keep it a thin wrapper.
**Tests required:** if you add a wrapper, a test that it produces a frame readable by `PlaybackSystem.ApplyFrame` identical to the equivalent `RecordDeltaFrame` call. If you reuse `RecordDeltaFrame`, no new test here (covered by Task 2/3).

### Task 2: `SubTickSnapshotRecorder` with bounded ring (NGS-1.2) — file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/SubTickSnapshotRecorder.cs` (NEW)
A self-contained recorder capturing whole-repo state per node within one tick. **Whole-repo capture is intentional** (blueprints can synchronously write managed components and OTHER entities' components — see memory `project-blueprint-cross-entity-sync-mutation`; do NOT scope to a single entity).

API (adjust names sensibly, keep intent):
- `BeginTick(EntityRepository repo)` — reset the ring; record a **keyframe baseline** of `repo` into an internal buffer; initialise the internal version cursor.
- `RecordNodeEntry(EntityRepository repo, string nodeId)` — capture the per-node delta and advance the memory clock so the upcoming node's writes are isolated at a new version. Store `(nodeId, delta-bytes)` in the ring.
- `int Count`, `string NodeIdAt(int index)`.
- Bounded ring (capacity e.g. 256). On overflow: drop oldest AND surface it (log / counter) — never silently lose frames without a signal.

**CRITICAL correctness note (version stamping):** writes stamp the chunk at the CURRENT `_globalVersion`; deltas capture chunks with `version > prevTick`. The ordering of `BumpMemoryVersion()` vs. delta-capture vs. the node's own writes determines whether each node's mutation is attributed to the correct frame. There is an easy off-by-one here. You MUST get the ordering right and PROVE it with the Task 3 counter test — a write made between two `RecordNodeEntry` calls must land in exactly one node's delta. Do not guess; iterate against the test.

### Task 3: Restore (NGS-1.3) — same file
- `RestoreTo(int nodeIndex, EntityRepository scratchRepo)` — reconstruct the world state **as of entering the node at `nodeIndex`** by replaying the keyframe baseline then deltas `[0..nodeIndex]` via `PlaybackSystem.ApplyFrame` into `scratchRepo`. Document the exact semantics (state = before that node's own effect).
- The caller owns `scratchRepo` (a reusable throwaway repo). Allocation on restore is acceptable (user-paced).

**Tests required (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/SubTickSnapshotRecorderTests.cs`, NEW) — assert REAL runtime values:**
1. **Counter semantics (the pin):** one entity with an unmanaged `int`-bearing test component. Value=5; `BeginTick`; `RecordNodeEntry("n0")`; mutate→6; `RecordNodeEntry("n1")`; mutate→7; `RecordNodeEntry("n2")`. Then restore each index into a fresh scratch repo and assert: index 0 → 5, index 1 → 6, index 2 → 7. (Mirrors the design's SetVariable→Delay semantics: pointer at node K shows state as of entering K.)
2. **Attribution (off-by-one guard):** assert a mutation performed *after* `RecordNodeEntry("nK")` and *before* `RecordNodeEntry("nK+1")` is reflected when restoring `nK+1` but NOT when restoring `nK`.
3. **Whole-repo / multi-entity:** two entities; mutate entity A at one node and entity B at the next; restoring each index shows the correct per-entity values for BOTH entities (proves capture is whole-repo, not single-entity).
4. **Managed component:** a managed test component mutated across nodes restores correctly (proves managed-chunk capture works; small per-node alloc is acceptable).
5. **SimulationTick frozen:** across all `RecordNodeEntry` calls, `repo.SimulationTick` is unchanged while `repo.GlobalVersion` advanced by the number of node entries.
6. **Ring overflow:** exceeding capacity drops oldest and exposes the drop (assert the signal), does not throw.

## Success Criteria
- [ ] NGS-1.1–1.3 implemented; `SubTickSnapshotRecorder` self-contained (no UI/DBM/blueprint-compiler deps).
- [ ] All 6 test scenarios pass with exact-value assertions.
- [ ] No change to the BATCH-00 hot path or version-clock semantics.
- [ ] Full affected suite green (below), `Failed: 0` except documented pre-existing reds.
- [ ] Report submitted.

## How to run tests (no regen flags)
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` (your new tests + regression)
- `dotnet test FDP/Engine/Fdp.Core.Tests` (only if you touched `RecorderSystem`)
Known pre-existing reds (NOT yours; do not mask, do not "fix" by weakening): `Hrot.Blueprints.Tests` 7 (incl. `TickFrame_1000Frames_AllocatesZeroBytes`); `Fdp.Core.Tests` 2 timing benchmarks. If a NEW failure appears, fix the root cause.

## Report Requirements (`reports/BATCH-01-REPORT.md`)
Per DEV-GUIDE §4, plus: the exact bump/capture/write ordering you chose and WHY it attributes writes correctly (reference the off-by-one); whether you reused `RecordDeltaFrame` or added a wrapper; per-entity & managed-capture confirmation; ring capacity choice; exact test counts; suggested commit message.

**Autonomy:** finish in one go — implement, test, fix root causes, loop until green, then report. Only stop on a genuine breaking design flaw (document precisely). You do NOT commit.
