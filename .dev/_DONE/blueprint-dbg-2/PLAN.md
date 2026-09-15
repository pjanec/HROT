# Plan — Node-Granular Blueprint Stepping (blueprint-dbg-2)

**Dev lead:** Opus (orchestrates; does NOT write feature code).
**Coders:** sonnet sub-agents via Agent tool (`subagent_type: general-purpose`, `model: sonnet`), per `.dev/.guides/DEV-LEAD-GUIDE_claude.md` / `DEV-GUIDE_claude.md`.
**Source of truth clone:** `D:\Work\IOS-IG-SimHost-FDP` (this one). BSA work in `-2` deferred. See memory `project-debugger-clone-divergence`.

## Goal
"Step" moves the execution pointer **between nodes** within a tick and shows entity state **as of that node**, instead of today's tick-granular pause. Read-only v1. Step + Step Back. Clock stays paused; navigate recordings.

## Verified design (full detail: memory `project-node-granular-stepping-design`)
- **Capture = sub-tick delta frames**, recorded per node at `OnNodeEnter` while debug-active.
- **Sub-tick dirty granularity via SEMANTIC SPLIT** of `EntityRepository`'s version clock:
  - `_globalVersion` = ECS memory-mutation clock (drives chunk versioning on the `GetComponentRW`/`GetRefRW` hot path). Sub-increments per node during debug.
  - `_simulationTick` (NEW) = semantic frame clock. Frozen during a debug tick.
  - `Tick()` bumps BOTH (normal frame). New `BumpMemoryVersion()` bumps ONLY `_globalVersion` (called per node by the debug session). **Zero added branch on the hot path.**
- **Capture mechanism:** new synchronous `RecorderSystem.RecordSubTickDelta` into a caller-owned flat buffer (bypass `AsyncRecorder`). Unmanaged chunks zero-alloc; dirty managed chunks allocate (acceptable while debugging).
- **Restore for inspection:** `scratchRepo.SyncFrom(_preTickSnapshot)` then sequential `PlaybackSystem.ApplyFrame` for deltas[0..K]; inspector reads scratchRepo.
- **Mid-tick ECB:** deferred structural ops are NOT in a mid-tick capture — show as not-yet-applied; optional pending-ops panel.
- **Ownership:** recorder ring lives in `BlueprintDebugSession` (owns `OnNodeEnter`, `_entityFilter`, `ExecutionHistory`), NOT `DataBreakpointManager`.

## Why the split is safe (verified)
All delta/sync diffing is RELATIVE (`HasChunkChanged: version > sinceVersion`, `SyncFrom: == srcVer`) — absolute value irrelevant, only monotonicity matters. Most direct `GlobalVersion` readers are **memory-version** consumers that stay correct on `_globalVersion`. Only **frame-clock** readers migrate to `SimulationTick`. RISK: a missed frame-clock reader breaks ONLY during debug (normal play keeps both in lockstep) — so the migration must be exhaustive + invariant-asserted.

## Slices (batches)
- **BATCH-00 — Engine semantic split** (foundational, fully headless). Add `_simulationTick`/`BumpMemoryVersion`; migrate frame-clock readers; exhaustive reader audit + invariant.
- **BATCH-01 — Sub-tick recorder + capture ring.** `RecordSubTickDelta`; ring in `BlueprintDebugSession.OnNodeEnter` (bump + capture); restore path.
- **BATCH-02 — Virtual-pointer navigation + inspector** (non-visual logic headless-testable). Step/StepBack over ring; inspector reads scratch repo; step-past-end → one real tick. Overlay highlight is the only VISUAL piece → user smoke in the morning.
- **BATCH-03 (optional) — Pending-ECB panel + guardrails.**

## Test strategy
Headless behavioral assertions — drive ticks/steps in tests and assert recorded state / cursor / field values. NO `BLUEPRINT_REGENERATE_SNAPSHOTS`. Loop until `Failed: 0` except the one documented pre-existing red `TickFrame_1000Frames_AllocatesZeroBytes`.

Affected test projects:
- `FDP/Engine/Fdp.Core.Tests` (version split, recorder, playback)
- `FDP/Engine/Fdp.ModuleHost.Tests` (snapshot providers, event accumulator)
- `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests` (HSM tick/trace)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` (debug session, stepping)
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests` (DBM)

## Autonomy boundary (this overnight run)
Opus delegates, reviews hard, runs the suite, commits. Stops to notify user only per DEV-LEAD-GUIDE §"Stop and notify": 3x failed review, design⇔code contradiction, detached-HEAD submodule, unrecoverable sub-agent error, or mission complete. Visual smoke (overlay) is the user's morning task.
