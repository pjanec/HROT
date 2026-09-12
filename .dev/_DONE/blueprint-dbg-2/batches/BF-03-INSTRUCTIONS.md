# BF-03: Step-past-end tick-bridge stalls at latent (Delay) nodes — "Not paused" dead state

**Type:** Corrective bug fix (medium — integration; use SONNET, not Zoo)   **Est:** ~6h
**Onboarding:** `.dev/.guides/DEV-GUIDE_claude.md`; `docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md` §3.4; `.dev/_DONE/blueprint-dbg-2/reviews/BATCH-05-REVIEW.md`. Corrects BATCH-05 (commit `05d1a10b`).

## Bug (reproduced by user, Count5)
Breakpoint on a node before a `Delay`. Tick → pause. Step Over within the tick until the pointer is on the **Delay** node (the last recorded node — the tick suspended there). One more Step Over → the Blueprint Tools panel shows **"Not paused"**, the Delay node keeps pulsing, the breakpoint never re-hits, and **no stepping buttons appear** — a dead state.

## Root cause
The BATCH-05 tick-bridge in `BlueprintDebugSession.StepForwardOrCF6` (end-of-recording branch) does:
`_isPaused=false; … ; _timeController.RequestStepOneTick();` and relies on the armed breakpoint re-firing within that one tick.

That holds only for **synchronous** ticks (the chain re-runs every tick). At a **latent boundary** (Delay / WaitForChannel) the tick ended *suspended inside the latent*; advancing exactly one tick just continues the latent countdown — the node chain does NOT re-run, so no probe/breakpoint fires that tick. `RequestStepOneTick` leaves the clock in paused/deterministic mode (one tick advanced) while `_isPaused=false` → dead state: not paused (no step UI), clock not progressing, latent node pulsing. A multi-tick latent cannot be crossed by a single-tick advance.

## Why the existing CF-6 path is the correct model
`BlueprintDebugSession.Step(StepMode)` (~line 973) already steps correctly across latents: it computes the paused node's exec-successors via `ExecSuccessors.GetSuccessors(graph, authoredNodeId)`, sets one-shot **temporary breakpoints** on them (`SetTemporaryBreakpoints`), and `RequestResume()`s. The temp BP fires whenever the successor next executes — i.e. when the Delay completes, however many ticks later — re-pausing on the post-latent node. Terminal node (no successors) → `Continue()`. This is exactly what step-past-end needs; it just must step **from the last recorded node**, not from `_pausedAt`.

Note: during within-tick navigation `_pausedAt` stays the ORIGINAL breakpoint node; the pointer (`CurrentNodeId`) is what tracks the last recorded node. So the bridge must use `CurrentNodeId` as the "from" node.

## The fix (prescribed)
1. **Extract** the CF-6 successor-stepping core from `Step(StepMode)` into a private helper, e.g.
   `private void StepFromNode(Guid assetId, Guid graphId, string fromNodeId, StepMode legacyFallback)`:
   - resolve graph (`_graphs[graphId]`), parse `fromNodeId` → authored Guid; on failure → `LegacyStepOneTick(legacyFallback)` (existing fallback).
   - `successors = ExecSuccessors.GetSuccessors(graph, authoredNodeId)`; if empty → `Continue()` and return.
   - else set temp BPs on successors (`BreakpointTarget(assetId, graphId, s)` via `SetTemporaryBreakpoints`), clear pause/nav state (`_isPaused=false; _pausedAt=null; _pausedOnEntity=null; _nodePointer=-1; _stepMode=None; _firedBreakpointsThisTick.Clear();`), keep `_recordingEntity`, `RequestResume()`, raise `OnSessionStateChanged`.
   - Have the existing `Step(StepMode)` call `StepFromNode(_pausedAt.AssetId, _pausedAt.GraphId, _pausedAt.NodeId, fallbackStepMode)` so its behavior is unchanged (verify CF-6 tests still pass).
2. **Replace the broken bridge** in `StepForwardOrCF6`'s end-of-recording branch: when at the last recorded node AND `RecordingActive`, call
   `StepFromNode(_pausedAt!.AssetId, _pausedAt!.GraphId, CurrentNodeId!, fallbackStepMode)` — i.e. step from the LAST RECORDED node (`CurrentNodeId`) to its successor via temp-BP + resume. Remove the `RequestStepOneTick` call from this path.
   - This crosses a Delay correctly (temp BP on the Delay's continuation fires when the Delay elapses), handles synchronous next-nodes, and Continue()s on a terminal last node — never leaving the dead state (clock always resumes; a temp BP or the user BP re-pauses).
   - Keep the no-breakpoint-armed branch as the existing clamp.
3. Confirm `ExecSuccessors.GetSuccessors` returns the Delay node's authored exec-successor (the continuation). If a latent node's successor is not resolvable this way, document precisely and stop — do not hack.

## Tests required (real compiled blueprint via `BlueprintTestFixture`; assert REAL behavior)
1. **Latent repro (the bug — primary):** build a blueprint with a `Delay` (latent) such that a breakpoint sits before the Delay and the tick suspends at the Delay (the Delay is the last recorded node). Pause; step the pointer to the Delay (last recorded node); call `StepInto()` (step past end). Assert:
   - the session **resumes** (not the dead state): a temp breakpoint was set OR the session is in a running/resumable state (`_isPaused == false` AND the time controller received a Resume, NOT a single one-tick step that strands it);
   - then drive ticks (`fixture.TickFrame` enough times to elapse the Delay) → the session **re-pauses** (`IsPaused == true`) on the post-Delay node (or wherever the temp BP / user BP next fires), with a fresh recording (`RecordedNodeCount >= 1`) and a valid pointer (`>= 0`);
   - explicitly assert it does NOT end stuck: after the Delay elapses it is paused again (regression guard for the reported dead state).
2. **Synchronous still works:** the BATCH-05 non-latent scenario still advances and re-pauses at the next tick (update the BATCH-05 `TickBridge`/`VirtualPointerTests` assertions that checked `RequestStepOneTick`/`StepRequestCount==1` to reflect the new resume/temp-BP mechanism — these are obsolete, update them to the correct semantics, do NOT delete coverage).
3. **Terminal last node:** when the last recorded node has no successors, step-past-end calls `Continue()` (resumes) and re-pauses at the next breakpoint hit.
4. **Regression:** CF-6 `Step()` fallback tests, within-tick Step/StepBack, and all existing debug tests stay green.

## Do-not-stop-until-green
Run the FULL affected suite (no regen flags), loop until `Failed: 0` except the documented pre-existing reds (`AiPrimitive_EmitMatchesGoldenSource` ×2, `Stage8_*` ×2, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_*Snapshot`, `WhenNode_ZeroAllocOnHotPath`):
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
- `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`
Any NEW failure ⇒ root-cause it. Transient `MapKeyboardKey.idl` build error ⇒ re-run.

## Constraints
- Touch `BlueprintDebugSession.cs` (refactor + bridge fix) and test files only. Do NOT change `ExecSuccessors`/CF-6 temp-BP machinery semantics, do NOT commit any `.bp.json`, do NOT suppress diagnostics or weaken tests.
- Do NOT commit. Report → `.dev/_DONE/blueprint-dbg-2/reports/BF-03-REPORT.md` (the refactor, the latent test design + how it proves no-dead-state, which BATCH-05 assertions were updated and why, exact test counts). The lead reviews and commits.
