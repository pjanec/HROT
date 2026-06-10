# Blueprint Subsystem — Debug DD Addendum: Node-Granular Stepping

> **Status:** Implemented (read-only inspection). Addendum to `Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md`.
> **Audience:** Implementation agent and human reviewer.
> **Delivered by:** branch `blueprint-integ-1`, commits `040f6f82` (BATCH-00), `c839c122` (BATCH-01), `7b1aae5b` (BATCH-02), `5007c22f` (BATCH-03). Working notes: `.dev/blueprint-dbg-2/`.
> **Companion code:** `Hrot.Blueprints.Core/Debug/SubTickSnapshotRecorder.cs`, `Hrot.Blueprints.Editor/BlueprintDebugSession.cs`, `FDP/Engine/Fdp.Core/EntityRepository.cs` (+ `FlightRecorder/`).

---

## 1. Problem: stepping was tick-granular

A compiled blueprint **tick is atomic**: the generated goto-state-machine runs the entire synchronous node chain in one tick, stopping only at a latent suspend (Delay / WaitForChannel) or Return. Probes (`DebugProbe.NodeEnter`) are non-blocking callbacks — they cannot halt the method mid-tick.

Pause/inspect uses `DataBreakpointManager`'s triple-buffer, **tick-granular** rewind (`_preTickSnapshot` / `_postTickSnapshot`): on a hit it rewinds the live repo to the start-of-tick state, and while paused the inspected view is that pre-tick state. **Consequence:** any pause *inside* a multi-node tick shows the same start-of-tick values. You cannot see "after `SetVariable`, before `Delay`" — there is no such snapshot. (CF-6 "stepping" via temporary breakpoints re-runs the whole tick and re-pauses, so the *overlay* advances a node but the *state shown* still jumps a full tick.)

## 2. Goal

"Step" / "Step Back" move an execution pointer **between nodes within the paused tick** and the inspector shows the entity state **as of entering that node** — e.g. for `Count = Count+1` then `Count = Count+1` in one tick, the pointer shows the pre-increment value at the first node and the post-first-increment value at the second. Read-only (inspection) in this iteration; no edit-and-continue.

## 3. Approach (as implemented): record per-node, navigate recordings while paused

Because the tick is atomic and cannot be halted mid-flight, we **record** state at each node as the tick runs, then let the user **navigate the recordings** while the clock stays paused. No re-execution → no nondeterminism; the recordings are ground truth.

### 3.1 Capture — full whole-repo snapshot per probe (`SubTickSnapshotRecorder`)
At every `OnNodeEnter` during a *debug-active* tick, serialize the **entire `EntityRepository`** (all entities, all components, managed + unmanaged) to a flat buffer via `RecorderSystem.RecordKeyframe` (the Flight Recorder's full-snapshot frame — "keyframe"). Each recorded node is a **complete, self-contained world snapshot**, stored in a bounded ring `[(nodeId, snapshotBytes), …]`.

- **Why full snapshot, not "the debugged entity's components only":** blueprint nodes can synchronously write managed components and *other entities'* components mid-tick (the generic `IrOp_GetComponent` lowers to `GetComponentRW<T>(arbitraryEntity)`), so partial capture is not sound. A whole-repo snapshot is always correct.
- **Why keyframe (full), not delta (incremental):** see §5 — delta capture cannot currently detect blueprint variable writes. Full snapshots sidestep change-detection entirely.
- **Cost:** one full-repo snapshot per node, **only while debug-active** (a breakpoint/temp is armed for the debugged entity). The unmanaged chunk path is a zero-alloc `Unsafe.CopyBlock`; managed component chunks serialize through `FdpAutoSerializer` (small per-chunk alloc). Normal (non-debug) runtime does zero recorder work.
- **Entity scope:** recording is gated to a single debugged entity (`BlueprintDebugSession.IsRecordingEntity`: `_recordingEntity` exact-match, else `_entityFilter`). This keeps the ring a single linear node sequence.

### 3.2 Navigation — virtual pointer over the ring (clock stays paused)
Because the tick is atomic, **at pause all of the paused tick's nodes are already recorded**. A virtual pointer (`BlueprintDebugSession._nodePointer`) indexes the ring:
- On pause it initializes to the breakpoint node's recorded index.
- `StepInto`/`StepOver`/`StepOut` move the pointer **forward** (clamped at the last node); a new `StepBack()` moves it **backward** (clamped at 0). The clock/time-controller is **not** touched.
- `Continue()` (and any real tick advance) clears the pointer.
- When no recordings exist for the paused entity, `Step*` falls back to the existing **CF-6 temporary-breakpoint** stepping (re-run to next node). Both paths coexist.
- Interface additions: `StepBack()`, `CurrentNodePointer`, `CurrentNodeId`, `RecordedNodeCount`.

### 3.3 Inspection — read the pointer's restored state
On each pointer move, the recorder restores the snapshot at the pointer into a reusable scratch `EntityRepository` (`RestoreRecordedNode` → `PlaybackSystem.ApplyFrame`). While the pointer is active, `GetCurrentStateSnapshot()` / `CaptureStateSnapshot` read field values from the **scratch** repo (an `ISimulationView`) instead of the live/pre-tick `_view`; the overlay highlights the pointer's node. When not navigating, inspection reads `_view` as before.

### 3.4 Bridge to real time (not yet implemented — see §7)
Stepping *past* the last recorded node of the tick should advance exactly one real tick (re-record, re-pause at the first node). Within-tick navigation works without it; this is a planned follow-up.

## 4. Engine support: the version-clock semantic split (BATCH-00)

`EntityRepository` was split into two monotonic clocks:
- **`_globalVersion`** — the ECS *memory-mutation* clock that `GetComponentRW` / `NativeChunkTable.GetRefRW` stamp chunk versions from (hot path, unchanged). A new `BumpMemoryVersion()` advances **only** this.
- **`_simulationTick`** (new) — the *semantic frame* clock. `Tick()` advances **both**; frame-index / wall-tick consumers (Flight Recorder frame headers, `ISimulationView.Tick`, HSM/BTree trace ticks, `DataBreakpointManager.PausedTick`) read `_simulationTick`. Memory-version consumers (snapshot-provider delta-skip, request-id nonces, etc.) stay on `_globalVersion`. Full classified reader audit: `.dev/blueprint-dbg-2/reports/BATCH-00-REPORT.md`.

This split lets the debugger advance the memory clock per node (for sub-tick chunk-version granularity) **without** polluting the semantic frame clock. **It is currently exercised only as scaffolding** — the active full-snapshot recorder does not depend on per-node deltas — but it is **deliberately retained** to enable the delta optimization in §5. With no `BumpMemoryVersion` calls, `_globalVersion == _simulationTick` and the split is behaviorally inert.

## 5. Future optimization: delta-frame capture

**Motivation.** Full-snapshot-per-node is simple and robust but stores a complete world per node; for large worlds × many nodes-per-tick this could be heavy (still debug-active-only). A delta path would store **one baseline keyframe per tick + only the changed chunks per node**, reconstructing node K by replaying baseline + deltas[0..K].

**Why it is not active.** Delta capture detects changes via chunk versions (`HasChunkChanged`: `chunkVersion > prevVersion`). But blueprint state lives in a `BlueprintBlackboard` component reinterpreted as a `ref State s` struct: the dispatch system fetches it via `GetComponentRW<BlueprintBlackboard>(self)` **once per tick** (one version stamp), and every state write (`SetVariable`, working-state, latent cursors) is `s.Field = …` straight into that pinned span — never re-stamping. So the blackboard chunk's version does **not** advance per node, and deltas miss every blueprint variable update. (Cross-entity writes, managed components, and channel writes *do* go through `GetComponentRW` and would be detected normally — the blackboard span is the sole bypass.)

**The contained fix (when we want it).** At each probe, after `BumpMemoryVersion()`, **force-stamp the debugged entity's blackboard chunk** to the new version (e.g. touch `GetComponentRW<BlueprintBlackboard{tier}>(self)`; `GetRefRW` re-stamps when the version differs). Then per-node deltas capture the whole blackboard chunk plus anything else genuinely mutated. Care points: select the entity's actual blackboard tier (1024 / 4096 / 16384); scope to the debugged entity; verify no additional direct-memory bypass paths exist. `SubTickSnapshotRecorder.RecordNodeEntry` would switch `RecordKeyframe` → one baseline keyframe + `RecordDeltaFrame` per node, and `RestoreTo` would replay baseline + deltas[0..K] (the recorder was originally written this way; see BATCH-01).

**Decision (2026-06-10):** ship full-snapshot-per-node now (correct, simple); keep the BATCH-00 version split in place as the hook; switch to deltas only if profiling shows the full-snapshot footprint/CPU is a problem.

## 6. Mid-tick ECB / deferred ops (limitation)
Structural operations (entity create/destroy, add/remove component) and event publishing are deferred through `IEntityCommandBuffer` / `FdpEventBus` and applied in the `Sync` phase — they are **not** present in a mid-tick snapshot. Node-granular inspection therefore shows such ops as *not yet applied* (which is the truthful sub-tick state — they genuinely haven't hit the repo). A future enhancement may surface queued ECB ops in a separate "Pending Mutations" panel.

## 7. Not yet implemented / follow-ups
- **Step-past-end tick-bridge** (§3.4): advance one real tick at the end of the recorded chain and re-pause at the first node.
- **Editor overlay** visual confirmation that the node highlight follows the virtual pointer (logic is in place; visual smoke pending).
- **Pending-ECB panel** (§6).
- **Delta-frame optimization** (§5), if needed for performance.
- **Unscoped multi-entity edge:** when neither `_recordingEntity` nor `_entityFilter` is set, recording is not entity-scoped (narrow; the debugger normally sets a filter).

## 8. Test coverage
- `FDP/Engine/Fdp.Core.Tests/VersionClockSplitTests.cs` — version split, view-tick freeze, recorder round-trip, invariant.
- `Hrot.Blueprints.Tests/Debug/SubTickSnapshotRecorderTests.cs` — capture ring + restore (counter 5/6/7, attribution, multi-entity whole-repo, managed component, overflow).
- `Hrot.Blueprints.Tests/Debug/SubTickRecorderIntegrationTests.cs` — recording during a real compiled-blueprint tick (armed/unarmed, frozen frame clock).
- `Hrot.Blueprints.Tests/Debug/VirtualPointerTests.cs` — pointer init/clamp/clear, exact per-node values (`A = 0 → 0 → 10` within one paused tick), entity-scope, CF-6 fallback.
