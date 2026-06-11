# Task Tracker — Node-Granular Stepping (blueprint-dbg-2)

Status: ⬜ todo · 🔄 in progress · ✅ done · ⚠️ needs fixes

## BATCH-00 — Engine semantic split ✅ DONE (committed, review APPROVED 2026-06-10)
- ✅ NGS-0.1 — Add `_simulationTick` + `SimulationTick`; `Tick()` bumps both; new `BumpMemoryVersion()` bumps only `_globalVersion`; `Set/ResetGlobalVersion` keep both aligned; init aligned.
- ✅ NGS-0.2 — Redirect `ISimulationView.Tick` → `_simulationTick`.
- ✅ NGS-0.3 — Migrated frame-clock readers to `SimulationTick` (RecorderSystem headers, DBM PausedTick, HsmTickSystem, BTreeTickSystem). ModuleHostKernel correctly kept on `GlobalVersion` (memory-version, justified in report).
- ✅ NGS-0.4 — Exhaustive reader audit (28 GlobalVersion + ~20 view.Tick readers classified in report) + `#if DEBUG` invariant `_globalVersion >= _simulationTick` in `BumpMemoryVersion()`. 15 new behavioral tests.

## BATCH-01 — Sub-tick recorder mechanism ✅ DONE (committed, review APPROVED 2026-06-10)
- ✅ NGS-1.1 — Reused `RecorderSystem.RecordDeltaFrame` directly (no wrapper; sound).
- ✅ NGS-1.2 — `SubTickSnapshotRecorder` (Blueprints.Core/Debug): keyframe baseline + bounded ring; `BeginTick`/`RecordNodeEntry` (capture→store→advance→bump, ordering proven).
- ✅ NGS-1.3 — `RestoreTo(nodeIndex, scratchRepo)`: keyframe + deltas[0..K]. 7 behavioral tests (counter 5/6/7, attribution, multi-entity whole-repo, managed, ST-frozen, overflow, reset). [OnNodeEnter wiring → BATCH-02.]

## BATCH-02 — Recorder wiring into live pipeline ✅ DONE (committed, review APPROVED w/ carried items 2026-06-10)
- ✅ NGS-2.0 — Wired `SubTickSnapshotRecorder` into `BlueprintDebugSession`: `SetLiveRepository` (EditorSubsystem), `BeginTick` in `OnNewTick` on `SimulationTick` change, `RecordNodeEntry` in `OnNodeEnter` behind `RecordingActive` gate; `RecordedNodeCount`/`RecordedNodeIdAt`/`RestoreRecordedNode` exposed. 4 integration tests (real compiled blueprint). ⚠️ Carries P1 (entity-scope recording) + P2 (tighten test) → BATCH-03 Corrective Task 0.

## BATCH-03 — Virtual-pointer navigation + inspector ✅ DONE (committed, review APPROVED 2026-06-10)
- ✅ NGS-2.0-CT0 — entity-scope recording (P1, `IsRecordingEntity`) + exact-value assertion (P2).
- ✅ NGS-2.1 — Virtual pointer Step/StepBack over the ring (clock paused); Step* remap with CF-6 fallback; clamps; cleared on Continue. Added `StepBack`/`CurrentNodePointer`/`CurrentNodeId` to interface.
- ✅ NGS-2.2 — Inspector reads pointer's restored scratch repo; exact per-node values proven (A=0→0→10). Reverts to live after Continue.
- ⚠️ **DESIGN PIVOT:** recorder now uses full KEYFRAME-per-node (delta missed blueprint SetVar writes that bypass GetComponentRW). Makes BATCH-00 semantic split largely vestigial → **USER DECISION pending** (revert BATCH-00 vs keep). See BATCH-03-REVIEW Finding.

## BATCH-04 — Editor UI surfacing ✅ DONE (committed, review APPROVED 2026-06-10)
- ✅ NGS-2.4a — Inspector pane shows pointer's per-node state while paused (`ResolveInspectorSnapshot`), else live.
- ✅ NGS-2.4b — Node highlight (`CurrentlyExecutingNode`) follows the virtual pointer while paused.
- ✅ NGS-2.4c — Step Back button + "node X / N" indicator (`FormatNodePosition`). 17 tests (exact per-node values, pointer tracking, boundary cases). Ready for human visual smoke.

## BATCH-05 — Step-past-end tick-bridge ✅ DONE (committed, review APPROVED 2026-06-10)
- ✅ NGS-2.3 — Step-past-last-node → resume exactly one tick (`RequestStepOneTick`); armed breakpoint re-fires → `HandleBreakpointHit` records new tick + re-pauses. Guarded on `RecordingActive`; no-arm keeps clamp. 5 TickBridge tests (advance+fresh-record+re-pause, no-arm guard, within-tick + CF-6 regression). P3: cross-tick value-distinct test (DBG2-D5).
- ⬜ NGS-2.4 — Overlay highlight follows pointer node (VISUAL — user smoke next morning).

## BPC-IMPLICIT-RETURN — Compiler feature (separate from debugger) ✅ DONE (committed, Zoo pro, review APPROVED 2026-06-10)
- ✅ `ReturnNode` now optional: implicit return at end-of-chain (`SealFallThrough` → `ReturnStatus(Success)` AiPrimitive/Library, `Return(null)` Instance) + empty-branch-arm sealing; BP1601 relaxed. Explicit Return kept for early-exit / non-default status/value. 6 behavioral tests.

## BPDBG-PERNODE-PROBES — Per-exec-node debug probes ✅ DONE (committed `a34de9cb`, sonnet, hard-review APPROVED 2026-06-11)
- ✅ Probes are now per-exec-node, not per-block: fused `SetVar → Delay` etc. each get their own `NodeEnter` probe. `IrDebugAnnotation.ExecEntryNodeId` (Stage5 tags exec entries; data nodes never) → `DebugProbeInsertion` emits per-node probes (latent recovered via post-WaitLowering `OriginNodeId`; header probe only for EventEntry/Sequence; first exec node keeps block-entry probe → single-node blocks byte-identical). `bpTargets` one-to-one (EventEntry falls back to block id). 6 `PerNodeProbesTests`. BF-03/BF-04 regression re-verified green.
- ✅ Test hygiene (committed `05ed27e4`): decoupled CF2/CF7rev from the user's scratch `Count4.bp.json` → frozen `TestAssets/Count4.bp.json` via direct deserialize (independent of production AssetRoots/scan, per the parallel main-toolbar-1 refactor constraint). Reverted scratch Count4 second-delay experiment (`1bc9537c`). Deleted non-asserting `CF1_NodeIdentityDiagnosticsTests`.

## BATCH-04 — Optional
- ⬜ NGS-3.1 — Pending-ECB "deferred ops" panel from `ThreadLocal<EntityCommandBuffer>`.
- ⬜ NGS-2.4 — Overlay highlight follows pointer node (VISUAL — user smoke).
