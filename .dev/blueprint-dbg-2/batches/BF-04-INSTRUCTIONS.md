# BF-04: step-past-end-of-tick lands on the breakpoint (Continue semantics) instead of the next tick's first node

**Type:** Corrective bug fix (integration; SONNET, not Zoo)   **Est:** ~6h
**Onboarding:** `.dev/.guides/DEV-GUIDE_claude.md`; `docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md` §3.4; `.dev/blueprint-dbg-2/reviews/BF-03-REVIEW.md`. Corrects BF-03 (`134eb197`).

## Bug (user smoke, Count5)
Graph (per-tick): `Entry → Sequence d3db6cd5 (Then0: increment SetVar [BP here], Then1: Sequence 00adf542 (SetVar, Delay → Return))`. Breakpoint on the Then0 increment SetVar. Hit → pause. Step down to the **Delay** (last recorded node). One more Step Over → **expected to land on the first Sequence node** (start of the next iteration) → **actually landed on the breakpoint SetVar**.

## Root cause
The Delay's only exec successor is `Return` (terminal). BF-03's `StepFromNode` `allSuccessorsAreTerminal` guard calls **`Continue()`**, which resumes to the next user **breakpoint** — so step-past-Delay behaved like Continue and stopped on the breakpoint. This violates the agreed principle: **"step past a Delay → node after the Delay; only Continue → next breakpoint."** When the post-latent path ends the tick (`Delay → Return`), the next node in execution order is the **first node of the next iteration** (the graph Entry's successor), reached when the tick restarts after the Delay completes — NOT the user breakpoint.

## The fix (prescribed)
The CF-6 `Step()` terminal behavior (`Continue()`) is correct for general within-tick stepping and must stay. The **tick-bridge** (step-past-last-recorded-node in `StepForwardOrCF6`) needs different end-of-tick handling: land on the **next tick's first node**, not the breakpoint.

In `StepForwardOrCF6`'s end-of-recording branch (where it currently calls `StepFromNode(_pausedAt.AssetId, _pausedAt.GraphId, CurrentNodeId!, …)`), change the logic so the **target** is computed as:
- successors of the LAST RECORDED node (`CurrentNodeId`) that are **non-terminal** → step to them (existing behavior; e.g. `Delay → SetVar` lands on `SetVar`); ELSE
- **end-of-tick (all successors terminal, e.g. `Delay → Return`)** → target = the graph's **first executable node(s)** = exec-successors of the graph's `EventEntryNode`. Set one-shot temp breakpoints on those and `RequestResume()`. The temp BP fires at the start of the next iteration (after the Delay completes and the tick restarts from Entry) → re-pause on the first node (the Sequence), NOT the user breakpoint.

Implementation notes:
- Find the entry node: the single `EventEntryNode` (kind `EventEntry`) in `graph.Nodes`; its `ExecSuccessors.GetSuccessors` = the first executable node(s) (e.g. the first `Sequence`). The probe identity for the entry block may be the successor's id (Stage5 `SourceNodeId` overwrite) — that's fine; `SetTemporaryBreakpoints`/`BreakpointTargets` translate authored→probe id as for CF-6.
- Suppress the user breakpoint while the temp BP is pending (existing `SetTemporaryBreakpoints` behavior) so the first-node temp BP wins over the user BP at the next iteration.
- Keep the no-breakpoint-armed clamp. Keep `StepFromNode`/CF-6 `Step()` general terminal→`Continue()` unchanged (only the bridge's end-of-tick path changes).
- If the graph has no `EventEntryNode` or no entry-successor (degenerate), fall back to the existing `Continue()` (document it).

## Tests required (real compiled blueprint via `BlueprintTestFixture`)
Use a graph where the **first node differs from the breakpoint node** so the assertion is discriminating (Count5's shape): `Entry → Seq(firstNode) → … → SetVar(BP) → … → Delay → Return`.
1. **Step-past-end-of-tick lands on the FIRST node, not the breakpoint (primary):** BP on a mid-chain node; pause; step to the Delay (last recorded); step past → drive ticks to elapse the Delay → assert the session re-pauses on the **graph's first executable node** (`CurrentNodeId` == entry's successor id), and explicitly assert it is **NOT** the breakpoint node id. This is the discriminating assertion the BF-03 test lacked.
2. **Non-terminal post-latent still lands on the successor:** `Delay → SetVar → Return` — step past the Delay lands on `SetVar` (BF-03 behavior preserved).
3. **No-dead-state regression:** still re-pauses (not stuck) after the Delay elapses.
4. **CF-6 `Step()` terminal → Continue() unchanged;** within-tick Step/StepBack unchanged; existing TickBridge/VirtualPointer tests green (update any that asserted the old Continue-to-BP end-of-tick behavior, with justification — do not delete coverage).

## Do-not-stop-until-green
`dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` + `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`, no regen flags, loop until `Failed: 0` except the documented pre-existing reds (`AiPrimitive_EmitMatchesGoldenSource` ×2, `Stage8_*` ×2, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_*Snapshot`, `WhenNode_ZeroAllocOnHotPath`). NEW failure ⇒ root-cause it. Transient `MapKeyboardKey.idl` build error ⇒ re-run.

## Constraints
Touch `BlueprintDebugSession.cs` + test files only. Do NOT change CF-6/ExecSuccessors semantics, do NOT commit any `.bp.json`, do NOT suppress diagnostics or weaken tests. Do NOT commit. Report → `.dev/blueprint-dbg-2/reports/BF-04-REPORT.md` (the end-of-tick target logic, how the first-node temp BP is computed, the discriminating test + exact landing node asserted, which prior assertions changed + why, test counts). The lead reviews and commits.
