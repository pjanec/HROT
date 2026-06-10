# BATCH-03: Virtual-pointer navigation + inspector redirect

**Tasks:** NGS-2.0-CT0 (Corrective), NGS-2.1, NGS-2.2   **Phase:** Navigation   **Est:** ~16h
**Dependencies:** BATCH-00/01/02 (`040f6f82`, `c839c122`, `7b1aae5b`).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-dbg-2/PLAN.md`.
3. `.dev/blueprint-dbg-2/reviews/BATCH-02-REVIEW.md` (Corrective Task 0 below comes from here) + `reports/BATCH-02-REPORT.md`.
4. This file.

## Key design fact (drives everything)
A compiled blueprint tick is ATOMIC: a breakpoint does NOT halt mid-tick — the whole node chain runs and the CLOCK pauses afterward (DBM rewinds to pre-tick). So **at pause, ALL of the paused tick's nodes are already recorded** in `SubTickSnapshotRecorder`. Node-granular "stepping" = move a virtual pointer over those recordings while the clock stays paused; the inspector reads the restored state at the pointer. No re-execution.

## Corrective Task 0 (from BATCH-02 review — do FIRST)
**CT0a (P1): Entity-scope the recording.** Today `RecordingActive` ignores `_entityFilter`, so multiple instrumented entities in one tick interleave into one ring. Fix: record only for the single debugged entity. Recommended: in `OnNodeEnter`, only `RecordNodeEntry` when `self` is the debugged entity. Define the debugged entity explicitly — when a breakpoint pauses, that entity (`_pausedOnEntity`) is the subject; during the live tick before a pause, scope to `_entityFilter` when set, otherwise to the entity that owns an armed breakpoint's asset. Pick a clear rule, document it, and TEST it: two entities ticking with a breakpoint armed → the ring contains only the debugged entity's nodes (assert node count + that restored values reflect only that entity's mutations, not the other's interleaving).
**CT0b (P2): Tighten the sub-tick proof.** The new navigation tests (NGS-2.1/2.2 below) must assert EXACT intermediate values (e.g. a node where A==10), which supersedes BATCH-02 Test 2's loose `<20` assertion. Update or replace that assertion.

## Tasks (after CT0)

### Task 1: Virtual pointer + Step/StepBack (NGS-2.1) — file: `BlueprintDebugSession.cs`
- Add a virtual-pointer index over the recorder's ring, valid only while `IsPaused` and recordings exist for the paused entity.
- **On pause** (where `_isPaused`/`_pausedAt` are set in `HandleBreakpointHit`): initialise the pointer to the ring index whose `NodeIdAt(index)` matches the paused node id (the breakpoint node). If not found, default to the last recorded index.
- Add **`void StepBack()`** to `IBlueprintDebugSession` + impl: pointer-- clamped at 0.
- Remap **`StepInto`/`StepOver`/`StepOut`** to pointer++ (clamped at `RecordedNodeCount-1`) **when node-granular recordings exist for the paused entity**; otherwise fall back to the existing CF-6 temp-BP stepping (do NOT break existing CF-6 tests — keep the fallback path intact and used when there are no recordings).
- Stepping does NOT touch the clock/time-controller in the node-granular path (clock stays paused).
- Stepping past the LAST recorded node is a NO-OP/clamp in this batch (the advance-one-real-tick bridge is BATCH-04). Document this.
- Expose for the UI: `int CurrentNodePointer { get; }`, `string CurrentNodeId { get; }` (the pointer's node id), and keep `RecordedNodeCount`.
- `Continue()` and a real tick advance must reset/clear the pointer.

**Tests required** (real compiled blueprint via `BlueprintTestFixture`, reuse the Sequence A:0→10→20 asset from BATCH-02's integration tests):
- Pause on a breakpoint → pointer initialised at the breakpoint node; `CurrentNodePointer`/`CurrentNodeId` correct.
- `StepBack()` moves pointer to earlier nodes; Step forward moves later; clamps at both ends (StepBack at 0 = no-op, Step at last = no-op).
- Pointer is cleared after `Continue()`.

### Task 2: Inspector reads the pointer's restored state (NGS-2.2) — file: `BlueprintDebugSession.cs`
- Maintain a reusable scratch `EntityRepository`. When the pointer moves (and on pause-init), restore the whole-repo state as-of the pointer's node into the scratch via `RestoreRecordedNode(pointer, scratch)`.
  - **Scratch registration:** the scratch needs the same component schema as the live repo for restore. Seed it once per pause via `scratch.SyncFrom(liveRepo)` (copies registrations + data) BEFORE the first `RestoreRecordedNode`, OR confirm `PlaybackSystem.ApplyFrame` auto-registers from the keyframe — verify which, and do whatever makes restore correct. Document the choice.
- Redirect inspection: when the pointer is active, `GetCurrentStateSnapshot()` / `CaptureStateSnapshot` must read component values from the SCRATCH repo (the pointer's node state), not the live/pre-tick `_view`. Recommended: an `ISimulationView _inspectionView` field that is the scratch repo while navigating and `_view` otherwise; route the existing `CaptureStateSnapshot` reads (`HasComponent`/`GetComponentRO`) through it.
- When not paused / no recordings, behavior is unchanged (reads `_view`).

**Tests required** (the headline behavioral proof — EXACT values):
- With the Sequence A:0→10→20 blueprint paused: at each pointer position, `GetCurrentStateSnapshot()` returns the field value `A` as-of entering that node. Assert the EXACT sequence across StepBack/Step: e.g. an early node → A=0, a later node → A=10, the last → A=20 (or the precise values dictated by the actual node ordering — assert them exactly, not a range). This is the feature's headline proof: the SAME paused tick shows DIFFERENT, correct per-node values as the pointer moves. (This also fulfils CT0b.)
- Inspector returns live/pre-tick state again after `Continue()`.
- Multi-entity (CT0a): with two entities, navigating the debugged entity shows only its values; the other entity's concurrent mutations don't appear in the debugged entity's per-node sequence.

## Success Criteria
- [ ] CT0a: recording entity-scoped; multi-entity test proves no cross-contamination.
- [ ] CT0b: exact intermediate-value assertion present.
- [ ] Virtual pointer Step/StepBack works while paused, clock untouched, clamps at ends, cleared on Continue.
- [ ] Inspector returns correct per-node state at the pointer (exact values); reverts to live after Continue.
- [ ] CF-6 temp-BP stepping fallback intact (existing CF6 tests green) when no recordings.
- [ ] Full affected suite green (`Failed: 0` except documented pre-existing reds).
- [ ] Report submitted.

## How to run tests (no regen flags)
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
- `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`
Known pre-existing reds (NOT yours): `Hrot.Blueprints.Tests` 7 (incl. `TickFrame_1000Frames_AllocatesZeroBytes`). NEW failure ⇒ root-cause it. NOTE: a transient first-build error about `MapKeyboardKey.idl` (DDS codegen in Hrot.Network.Orchestration) can occur — just re-run; it is unrelated.

## Report Requirements (`reports/BATCH-03-REPORT.md`)
Per DEV-GUIDE §4, plus: the entity-scoping rule chosen; pointer init logic; how Step* remap coexists with the CF-6 fallback; the scratch-repo registration approach (SyncFrom vs auto-register) and why; the exact per-node value sequence asserted; test counts; suggested commit message.

**Autonomy:** finish in one go — investigate, implement, test, fix root causes until green, then report. Only stop on a genuine breaking design flaw (document precisely). You do NOT commit.
