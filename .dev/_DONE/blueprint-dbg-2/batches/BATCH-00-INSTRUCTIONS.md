# BATCH-00: Engine semantic version split (frame clock vs memory clock)

**Tasks:** NGS-0.1, NGS-0.2, NGS-0.3, NGS-0.4   **Phase:** Engine foundation   **Est:** ~12h
**Dependencies:** none (foundational)

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — how you work (codebase-memory MCP first; tests verify real values; loop until green; report honestly).
2. `.dev/_DONE/blueprint-dbg-2/PLAN.md` — the feature & why this split exists.
3. This file.

## Background (the problem you are solving)
Node-granular blueprint stepping needs **sub-tick dirty granularity**: within ONE atomic tick, each blueprint node may mutate components, and we must tell which chunks changed between node A and node B. Today chunk versions are stamped from `EntityRepository._globalVersion`, which only advances once per tick (`Tick()` → `Interlocked.Increment`). So all mid-tick writes share one version → indistinguishable.

**Fix (semantic split):** separate the ECS *memory-mutation clock* from the *semantic frame clock*.
- `_globalVersion` stays the memory clock that the `GetComponentRW`/`NativeChunkTable.GetRefRW` hot path stamps from. It may be advanced MID-TICK (per node) during debug.
- A NEW `_simulationTick` becomes the frame clock that all *frame-index / wall-tick* consumers read. It is frozen during a mid-tick debug burst.

This must add **zero branches** to the `GetComponentRW`/`GetRefRW` hot path — you do NOT touch those methods. You only add a new advance method and split which field the frame-clock consumers read.

## Verified facts (do not re-derive; confirm if editing nearby)
- `EntityRepository.Tick()` = `Interlocked.Increment(ref _globalVersion)` only — `FDP/Engine/Fdp.Core/EntityRepository.cs:142`.
- `GlobalVersion` property — `EntityRepository.cs:137`. `SetGlobalVersion` (internal) `:150`. `ResetGlobalVersion` `:160`.
- `ISimulationView.Tick => _globalVersion` — `FDP/Engine/Fdp.Core/EntityRepository.View.cs:27`.
- `NativeChunkTable.GetRefRW(id, currentVersion)` stamps `_chunkVersions[chunk] = currentVersion` (only if different) — `NativeChunkTable.cs:158`. **Do not modify.**
- Flight Recorder writes the frame index from `GlobalVersion`: `RecorderSystem.cs:63` (`RecordDeltaFrame`) and `:340` (`RecordKeyframe`) — `writer.Write((ulong)repo.GlobalVersion)`.
- Restore sets it back: `PlaybackSystem.cs:30` `repo.SetGlobalVersion((uint)tick)`; `PlaybackSystem.cs:374` `table.SetChunk(chunkIndex, chunkData, repo.GlobalVersion)`.
- Diffing is RELATIVE everywhere (`HasChunkChanged: version > sinceVersion`; `SyncFrom: == srcVer`) → absolute value of `_globalVersion` is irrelevant to correctness; only monotonicity matters.

## Tasks — complete IN ORDER. Do NOT start a task until the prior task's code + tests are done and the FULL affected suite is green.

### Task 1: Version-clock split (NGS-0.1) — file: `FDP/Engine/Fdp.Core/EntityRepository.cs` (UPDATE) + `EntityRepository.View.cs` if needed
- Add `private uint _simulationTick;` initialised to the SAME value `_globalVersion` is initialised to (find the ctor/init; keep them equal at construction).
- Add `public uint SimulationTick => _simulationTick;`.
- Modify `Tick()` to advance BOTH: `Interlocked.Increment(ref _globalVersion); Interlocked.Increment(ref _simulationTick);` (preserves all current frame-boundary behavior — both stay equal in normal execution).
- Add `public void BumpMemoryVersion() { System.Threading.Interlocked.Increment(ref _globalVersion); }` with an XML doc explaining it advances ONLY the memory clock for sub-tick dirty granularity (debug use), leaving the frame clock frozen.
- Make `SetGlobalVersion` and `ResetGlobalVersion` keep BOTH fields aligned (set/reset `_simulationTick` to the same value), so playback/test-reset paths don't desync the clocks. (Restore semantics: a restored frame's tick is both its memory version and its frame index.)
- Document the invariant in XML/comment: **`_globalVersion >= _simulationTick` always**; they are equal until the first `BumpMemoryVersion()`; only `BumpMemoryVersion()` makes the memory clock run ahead.

**Tests required** (`FDP/Engine/Fdp.Core.Tests`, new file e.g. `VersionClockSplitTests.cs`):
- `Tick()` advances `GlobalVersion` and `SimulationTick` by exactly 1 each; they remain equal after N normal ticks.
- `BumpMemoryVersion()` advances `GlobalVersion` by 1 and leaves `SimulationTick` UNCHANGED.
- After K `BumpMemoryVersion()` then one `Tick()`: `GlobalVersion == start+K+1`, `SimulationTick == start+1`.
- `SetGlobalVersion(v)` and `ResetGlobalVersion(v)` set BOTH to `v`.
- Invariant: `GlobalVersion >= SimulationTick` holds across a mixed sequence of `Tick()`/`BumpMemoryVersion()`.

### Task 2: Redirect the view's frame clock (NGS-0.2) — file: `FDP/Engine/Fdp.Core/EntityRepository.View.cs` (UPDATE)
- Change `ISimulationView.Tick` to return `_simulationTick` (not `_globalVersion`).

**Tests required:**
- `((ISimulationView)repo).Tick == repo.SimulationTick` after normal ticks.
- After `BumpMemoryVersion()` calls, `((ISimulationView)repo).Tick` stays frozen (equals `SimulationTick`), while `repo.GlobalVersion` advances. **This is the core guarantee — assert both.**

### Task 3: Migrate frame-clock consumers (NGS-0.3)
Migrate every consumer that uses the version as a **frame index / wall-tick** to read `SimulationTick` instead of `GlobalVersion`. Use codebase-memory `search_graph`/`trace_path` to locate exact lines. Known targets (verify each):
- `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs:63` and `:340` — write `(ulong)repo.SimulationTick` as the frame header (NOT `GlobalVersion`).
- `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackSystem.cs:30` — `SetGlobalVersion` already aligned by Task 1; confirm restore sets both (no extra change if Task 1 covers it). `:374` `SetChunk(..., repo.GlobalVersion)` is a MEMORY-version use → leave on `GlobalVersion`.
- `HsmTickSystem` — the `(ushort)` cast feeding `HsmTraceContext.CurrentTick` → `SimulationTick`.
- `BTreeTickSystem` — the `(int)` cast feeding `BTreeContext._frameCount` → `SimulationTick`.
- `DataBreakpointManager.OnHit` — `PausedTick` fallback `(long)_preTickSnapshot.GlobalVersion` → `SimulationTick`.
- `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs:540` (`_eventAccumulator.CaptureFrame(..., GlobalVersion)`) and `:700` (`LastRunTick`) — classify: these align frame-event accumulation to the frame clock → migrate to `SimulationTick` UNLESS your analysis shows they pair with a memory-version delta-skip (justify in report either way).

**Leave on `GlobalVersion`** (memory-version consumers — DO NOT change; changing them is a bug):
- `SharedSnapshotProvider`, `OnDemandProvider`, `DoubleBufferProvider` delta-skip (`_lastSeenTick`/`_lastSyncTick`).
- `NavigationIntentBridgeSystem` delta-skip + `requestId` nonce; `PathfindingActionNode` `requestId` nonce.
- `HierarchyOrderSystem.TopologyVersion`; `QueryDelta*`; `PlaybackSystem.SetChunk`.

**Tests required:**
- Flight Recorder round-trip: record a keyframe, advance with `Tick()`, record a delta, replay via `PlaybackSystem.ApplyFrame` into a fresh repo → restored component values correct AND the restored frame index matches `SimulationTick` (not an inflated `GlobalVersion`). Then: with `BumpMemoryVersion()` calls inserted before the delta capture, the frame header still equals `SimulationTick` (frozen) while the delta correctly captures the chunk mutated after the bump.
- A focused test per migrated consumer where feasible (e.g., HSM/BTree trace timestamp reads `SimulationTick`; DBM `PausedTick` reflects the frame clock). If a consumer is impractical to unit-test in isolation, state that in the report and cover it via the recorder round-trip + the existing suite.

### Task 4: Exhaustive reader audit + invariant (NGS-0.4)
- Grep the WHOLE repo (production code, not tests) for readers of `.GlobalVersion` and `ISimulationView.Tick` / `view.Tick` / `.Tick`. Classify EVERY production reader as **memory-version** (stays) or **frame-clock** (migrated) in a table in your report. This audit is the highest-value deliverable — a missed frame-clock reader breaks only during debug and the suite won't catch it.
- Add a debug-time invariant assert (`Debug.Assert` or equivalent guarded by `#if DEBUG`) that `_globalVersion >= _simulationTick`, placed where it can't fire on the hot path (e.g., in `BumpMemoryVersion`/`Tick`).

**Tests required:** a test asserting the invariant holds after a representative mixed sequence; and a regression assertion that with NO `BumpMemoryVersion` calls, `GlobalVersion == SimulationTick` after arbitrary normal ticking (proves normal play is unaffected).

## Success Criteria
- [ ] NGS-0.1–0.4 implemented per spec.
- [ ] `GetComponentRW`/`NativeChunkTable.GetRefRW` hot path UNCHANGED (no new branches/params).
- [ ] All migrated consumers read `SimulationTick`; all memory-version consumers untouched; classification table in the report.
- [ ] FULL affected suite green (projects below) — `Failed: 0` except the one documented pre-existing red `TickFrame_1000Frames_AllocatesZeroBytes`.
- [ ] Report submitted answering Report Requirements.

## How to run tests (no regen flags)
Run each affected project and fix root causes until green:
- `dotnet test FDP/Engine/Fdp.Core.Tests`
- `dotnet test FDP/Engine/Fdp.ModuleHost.Tests`
- `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests`
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
- `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`
Also build/test any BTree runtime test project you touch. The ONLY acceptable remaining failure is `TickFrame_1000Frames_AllocatesZeroBytes` (pre-existing). If anything else is red, fix the root cause — do not mask it.

## Report Requirements (`reports/BATCH-00-REPORT.md`)
Per DEV-GUIDE §4, plus specifically:
- **The full classified reader-audit table** (every production `GlobalVersion`/`.Tick` reader → memory-version|frame-clock → migrated? → justification).
- Any consumer where you deviated from the known-targets list, with reasoning.
- Confirmation the hot path was not modified.
- Exact test counts per project + the key behavioral scenarios.
- Suggested commit message.

**Autonomy:** Work to completion without asking permission. Run tests, fix root causes, loop until green, then report. Only stop on a genuine breaking design flaw (e.g., a frame-clock consumer that cannot be migrated without a hot-path change) — and if so, document it precisely in the report and return.
