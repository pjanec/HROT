# BATCH-05: Step-past-end tick-bridge (advance one real tick at end of recording)

**Tasks:** NGS-2.3   **Phase:** Navigation   **Est:** ~8h
**Dependencies:** BATCH-00..04 + BF-01/02 (latest commit on `blueprint-integ-1`).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/_DONE/blueprint-dbg-2/PLAN.md` + `docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md` (§3.4 is this feature).
3. `.dev/_DONE/blueprint-dbg-2/reviews/BATCH-03-REVIEW.md` / `BATCH-04-REVIEW.md`.
4. This file.

## Objective
Today, stepping forward at the LAST recorded node of the paused tick is a no-op (clamp) — see `BlueprintDebugSession.StepForwardOrCF6`, the `else: clamped at end` branch (~line 918). Make it instead **advance exactly one real tick**, record that tick, and re-pause at its start so the user can keep stepping into the next tick. Within-tick navigation (Step/StepBack) is unchanged.

## Verified mechanism (use this — do not invent another)
- `IEngineDebugTimeController.RequestStepOneTick()` advances the sim clock by exactly one tick and stays clock-paused (`MasterSyncTimeControllerAdapter` → `_masterSync.Step`). In production it runs the tick synchronously; in tests the `MockTimeController` is a no-op and the test drives the advance with `fixture.TickFrame(...)`.
- A breakpoint is still armed while paused (that's why we're paused), so advancing one tick re-hits it: `OnNewTick` fires `recorder.BeginTick`, `OnNodeEnter` records each node, and `HandleBreakpointHit` sets `_isPaused`, re-pauses the clock, and calls `InitNodePointerOnPause` — re-establishing a fresh per-tick recording + pointer. **Reuse this existing re-pause path; do NOT add post-step code that assumes the tick already ran** (that would break the mock/test path and is fragile in production).

## Task: tick-bridge at end-of-recording (NGS-2.3) — file: `Hrot.Blueprints.Editor/BlueprintDebugSession.cs`, `StepForwardOrCF6`
Replace the end-of-recording no-op with a one-tick advance, GUARDED so we only do it when a re-pause is guaranteed (a breakpoint is armed — i.e. `RecordingActive`). Concretely, in the node-granular branch when `_nodePointer == last`:
- If `RecordingActive` (a user/temp breakpoint is armed): clear per-tick navigation state and resume exactly one tick:
  - `_isPaused = false; _pausedAt = null; _pausedOnEntity = null; _nodePointer = -1; _firedBreakpointsThisTick.Clear();`
  - (Leave `_recordingEntity` as-is so the next tick records the SAME debugged entity.)
  - `_timeController.RequestStepOneTick();`
  - `OnSessionStateChanged?.Invoke();`
  - The armed breakpoint re-fires on the advanced tick → `HandleBreakpointHit` records the new tick and re-pauses with the pointer re-initialised (existing path).
- Else (no breakpoint armed — cannot guarantee re-pause): keep the current no-op clamp. Document why.
- Do NOT change within-tick stepping, StepBack, the CF-6 fallback, or the inspector.

Add a short XML-doc note on the new branch explaining the handshake and that the re-pause is driven by the armed breakpoint via `HandleBreakpointHit` (so it works under both the real time controller and the test fixture).

## Tests required (real compiled blueprint via `BlueprintTestFixture`; assert REAL runtime values)
Build a blueprint that **mutates a variable every tick** so cross-tick advancement is observable — e.g. `Entry → Sequence(SetVariable A = literal 10, SetVariable A = literal 20) → Return` is fine (A ends each tick at 20), but to prove a *new tick ran* use a per-tick-changing value: simplest is a blueprint whose tick sets `A` from a GetVariable+increment, OR assert via the recorded node SNAPSHOTS across two ticks. Pick whatever makes the cross-tick assertion unambiguous and document it.

1. **Tick-bridge advances exactly one tick and re-pauses with a fresh recording:** arm a breakpoint, `TickFrame` → pause (tick N recorded). Step the pointer to the last recorded node. Call `StepInto()` (the bridge). Then drive the advance (`fixture.TickFrame(...)` — mirrors the CF-6 fallback test pattern). Assert: the session is paused again; `SimulationTick` advanced by exactly 1 versus the first pause; `RecordedNodeCount` reflects the NEW tick (a fresh `BeginTick` happened — not appended to the old ring); and the pointer is valid (≥ 0).
2. **Inspector reflects the new tick (the cross-tick proof):** at the re-paused pointer, `GetCurrentStateSnapshot()` returns the variable value as-of the NEW tick — assert the EXACT value that proves a full additional tick executed (e.g. a per-tick counter is one higher than at the equivalent node of the previous tick). This is the discriminating assertion — not just "paused again".
3. **No-arm guard:** stepping past the last node with NO breakpoint armed (no recordings) still uses the CF-6 fallback / clamp and does not crash or spuriously resume.
4. **Regression:** existing VirtualPointerTests / SubTickRecorderIntegrationTests / CF6 tests stay green (within-tick Step/StepBack and CF-6 fallback unchanged).

## Success Criteria
- [ ] End-of-recording forward step advances exactly one real tick and re-pauses with a fresh per-tick recording + valid pointer (when a breakpoint is armed).
- [ ] Cross-tick inspector value proven (Test 2, exact value).
- [ ] No-arm case unchanged; within-tick nav + CF-6 fallback unchanged.
- [ ] Full affected suite green (`Failed: 0` except the documented pre-existing reds).
- [ ] Report submitted.

## How to run tests (no regen flags)
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
- `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`
Documented pre-existing reds (NOT yours): `AiPrimitive_EmitMatchesGoldenSource` (×2), `Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_GeneratedSource_Snapshot`, `WhenNode_ZeroAllocOnHotPath`. NEW failure ⇒ root-cause it. Transient `MapKeyboardKey.idl` build error ⇒ re-run.

## Report Requirements (`reports/BATCH-05-REPORT.md`)
Per DEV-GUIDE §4, plus: the exact bridge code + guard; how re-pause is driven (HandleBreakpointHit, works under mock + real controller); the cross-tick test design and the exact values asserted; whether `SimulationTick` advanced by exactly 1; test counts; suggested commit message.

**Autonomy:** finish in one go — implement, test, fix root causes until green, then report. Only stop on a genuine breaking design flaw (document precisely). Do NOT commit `.bp.json` experiment files. You do NOT commit — the lead commits.
