# Task Tracker — Node-Granular Stepping (blueprint-dbg-2)

Status: ⬜ todo · 🔄 in progress · ✅ done · ⚠️ needs fixes

## BATCH-00 — Engine semantic split ✅ DONE (committed, review APPROVED 2026-06-10)
- ✅ NGS-0.1 — Add `_simulationTick` + `SimulationTick`; `Tick()` bumps both; new `BumpMemoryVersion()` bumps only `_globalVersion`; `Set/ResetGlobalVersion` keep both aligned; init aligned.
- ✅ NGS-0.2 — Redirect `ISimulationView.Tick` → `_simulationTick`.
- ✅ NGS-0.3 — Migrated frame-clock readers to `SimulationTick` (RecorderSystem headers, DBM PausedTick, HsmTickSystem, BTreeTickSystem). ModuleHostKernel correctly kept on `GlobalVersion` (memory-version, justified in report).
- ✅ NGS-0.4 — Exhaustive reader audit (28 GlobalVersion + ~20 view.Tick readers classified in report) + `#if DEBUG` invariant `_globalVersion >= _simulationTick` in `BumpMemoryVersion()`. 15 new behavioral tests.

## BATCH-01 — Sub-tick recorder + capture ring
- ⬜ NGS-1.1 — `RecorderSystem.RecordSubTickDelta(repo, prevVersion, writer)` synchronous, caller-owned buffer.
- ⬜ NGS-1.2 — Capture ring in `BlueprintDebugSession`; `OnNodeEnter` (debug-active) calls `BumpMemoryVersion()` + records a delta.
- ⬜ NGS-1.3 — Restore: `scratchRepo.SyncFrom(_preTickSnapshot)` + sequential `ApplyFrame` to reconstruct node K.

## BATCH-02 — Virtual-pointer navigation + inspector
- ⬜ NGS-2.1 — Virtual pointer Step/StepBack over the ring (clock paused).
- ⬜ NGS-2.2 — Inspector (`CaptureStateSnapshot`) reads the pointer's restored scratch repo while pointer active.
- ⬜ NGS-2.3 — Step-past-last-node → advance exactly one real tick, re-record, re-pause at first probe.
- ⬜ NGS-2.4 — Overlay highlight follows pointer node (VISUAL — user smoke next morning).

## BATCH-03 — Optional
- ⬜ NGS-3.1 — Pending-ECB "deferred ops" panel from `ThreadLocal<EntityCommandBuffer>`.
