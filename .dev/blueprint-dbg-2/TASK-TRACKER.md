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

## BATCH-03 — Virtual-pointer navigation + inspector  (was BATCH-02 NGS-2.x)
- ⬜ NGS-2.0-CT0 — Corrective Task 0: entity-scope recording (P1) + tighten integration test to exact intermediate value (P2).
- ⬜ NGS-2.1 — Virtual pointer Step/StepBack over the ring (clock paused).
- ⬜ NGS-2.2 — Inspector (`CaptureStateSnapshot`) reads the pointer's restored scratch repo while pointer active.
- ⬜ NGS-2.3 — Step-past-last-node → advance exactly one real tick, re-record, re-pause at first probe.
- ⬜ NGS-2.4 — Overlay highlight follows pointer node (VISUAL — user smoke next morning).

## BATCH-04 — Optional
- ⬜ NGS-3.1 — Pending-ECB "deferred ops" panel from `ThreadLocal<EntityCommandBuffer>`.
- ⬜ NGS-2.4 — Overlay highlight follows pointer node (VISUAL — user smoke).
