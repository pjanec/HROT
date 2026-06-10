# BATCH-02: Wire sub-tick recorder into the live debug pipeline

**Tasks:** NGS-2.0   **Phase:** Integration (recording during a real tick)   **Est:** ~12h
**Dependencies:** BATCH-00 (`040f6f82`), BATCH-01 (`c839c122`).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-dbg-2/PLAN.md`.
3. `.dev/blueprint-dbg-2/reviews/BATCH-01-REVIEW.md` + `reports/BATCH-01-REPORT.md` (the recorder you are wiring in).
4. This file.

## Objective (ONLY this — do NOT add navigation/inspector/overlay; those are later batches)
Make a **real, debug-active compiled blueprint tick** drive `SubTickSnapshotRecorder` so that after the tick you can `RestoreTo(K)` and get the correct ECS state as-of entering node K. Prove it with an end-to-end integration test using a real compiled blueprint. No virtual-pointer Step/StepBack, no inspector redirect, no overlay in this batch.

## Investigation first (use codebase-memory MCP, then read)
Resolve these before coding; document findings in the report:
1. **Which `BlueprintDebugSession`?** It is referenced as `Hrot.Blueprints.Core.Debug.BlueprintDebugSession` (e.g. `EditorSubsystem.cs:904`, `ProbeIntegrationTests.cs`) but the file lives under `Hrot.Blueprints.Editor`. Confirm the actual class/namespace/file you must edit (the one with `OnNodeEnter`, `_view`, `_dataBreakpointManager`, `_entityFilter`).
2. **Live repo access.** The session holds `_view` (an `ISimulationView`, often a wrapper — tests pass `FakeSimulationView`), NOT a concrete `EntityRepository`. `BumpMemoryVersion()` and the recorder need the concrete live `EntityRepository`. `EditorSubsystem` owns it (`_world = new EntityRepository()`, ~line 519) and constructs the session (~line 904). Decide the cleanest hookup — preferred: add `BlueprintDebugSession.SetLiveRepository(EntityRepository)` and call it where `SetDataBreakpointManager` is called (EditorSubsystem + the DBM test harness in `Hrot.Diagnostics.Breakpoints.Tests` which has `_liveRepo`). Do NOT cast `_view`; obtain the real repo explicitly.
3. **Tick-boundary hook.** `DebugProbe.NewTick` is called by `BlueprintTestFixture.TickFrame` (see `ProbeIntegrationTests`). Determine whether the session already receives a per-tick "new tick" signal or whether to detect a tick boundary in `OnNodeEnter` by observing `_view.Tick` (= `SimulationTick`) change. Either is acceptable; pick the one that fits the existing wiring and justify.

## Tasks

### Task 1: Recording gate + lifecycle (NGS-2.0a) — file: the real `BlueprintDebugSession`
- Add a `SubTickSnapshotRecorder` instance to the session and a `SetLiveRepository(EntityRepository)` setter.
- Define a clear **RecordingActive** predicate: record only when the session is "armed" for the executing entity — i.e. there is at least one enabled user breakpoint OR temp breakpoint for the asset/graph being ticked AND the entity passes `_entityFilter` (or filter unset). When NOT armed, do ZERO recorder work (no keyframe, no bump) — normal runtime overhead must be unchanged.
- **BeginTick** at the tick boundary (per investigation #3) when RecordingActive: call `recorder.BeginTick(liveRepo)`.
- In `OnNodeEnter`, when RecordingActive: call `recorder.RecordNodeEntry(liveRepo, nodeId)`. This bumps `_globalVersion` per node (frame clock stays frozen). Keep this AFTER the existing history `Record(...)` and overlay logic; do not disturb the CF-6 temp-BP / `_isPaused` flow.
- Expose read access for later batches: `int RecordedNodeCount`, `string RecordedNodeIdAt(int)`, and `void RestoreRecordedNode(int index, EntityRepository scratchRepo)` delegating to the recorder. (Navigation/inspector consume these in BATCH-03 — just expose them now, no UI.)

### Task 2: Guard the live repo / null-safety (NGS-2.0b)
- If `SetLiveRepository` was never called (e.g. older construction paths, some tests), RecordingActive must be false and nothing should NPE. Fail safe and silent-OFF for recording is acceptable HERE (recording is an optional debug aid) — but log once at debug level if a breakpoint is armed yet no live repo is wired (so the gap is visible, per "no silent failure" spirit).

## Tests required — end-to-end with a real compiled blueprint
Use `BlueprintTestFixture` (see `Hrot.Blueprints.Tests/Debug/ProbeIntegrationTests.cs` and `CF6_SteppingTests.cs` for the pattern: build asset → compile → set sink/session → `TickFrame`). Assert REAL restored runtime values.

1. **Recording off when unarmed:** run a debug-instrumented blueprint tick with NO breakpoint set → `RecordedNodeCount == 0` and `liveRepo.GlobalVersion`/`SimulationTick` advance in lockstep (no extra bumps). Proves zero overhead when not debugging.
2. **Recording on when armed (the integration pin):** build a blueprint whose tick runs a multi-node synchronous chain that mutates a blueprint variable/component more than once in the tick (e.g. two increments of a Count on `self`). Arm a breakpoint so RecordingActive is true. Run one `TickFrame`. Then for each recorded node, `RestoreRecordedNode(K, scratch)` and assert the variable value equals the expected pre-node value (the SetVariable→… semantics: earlier node shows pre-increment, later node shows post-increment). This is the whole point of the feature — it must show DIFFERENT values across nodes within ONE tick, unlike today's tick-granular pause.
3. **SimulationTick frozen during the recorded tick:** assert `SimulationTick` did not change across the in-tick node bumps (only advanced by the one real `Tick()`), while `GlobalVersion` advanced by (1 + recorded node count).
4. **Multi-entity safety (if feasible with the harness):** two debugged entities in the same tick don't corrupt each other's recordings; otherwise document why deferred.

## Success Criteria
- [ ] Live repo wired; RecordingActive gate correct (off when unarmed → zero recorder work).
- [ ] Real compiled-blueprint tick produces per-node recordings; `RestoreRecordedNode` yields correct, DIFFERING per-node values within one tick (Test 2).
- [ ] No disturbance to CF-6 stepping / `_isPaused` / existing breakpoint behavior — existing debug tests still green.
- [ ] Full affected suite green (`Failed: 0` except documented pre-existing reds).
- [ ] Report submitted (investigation findings + chosen hookup + gate predicate + test counts).

## How to run tests (no regen flags)
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
- `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`
- `dotnet test FDP/Engine/Fdp.Core.Tests` (if you touched engine).
Pre-existing reds (NOT yours): `Hrot.Blueprints.Tests` 7 (incl. `TickFrame_1000Frames_AllocatesZeroBytes`); `Fdp.Core.Tests` 2 timing benchmarks. A NEW failure = root-cause it.

## Report Requirements (`reports/BATCH-02-REPORT.md`)
Per DEV-GUIDE §4, plus: the resolved class/namespace; the live-repo hookup chosen and every call site updated; the exact RecordingActive predicate and where BeginTick fires; confirmation normal (unarmed) runtime does ZERO recorder work; the Test-2 per-node values proving sub-tick state differences; exact test counts; suggested commit message.

**Autonomy:** finish in one go — investigate, implement, test, fix root causes, loop until green, then report. If the live-repo hookup turns out to require a deeper architectural change than a setter (genuine breaking design flaw), STOP and document precisely in the report rather than hacking. You do NOT commit.
