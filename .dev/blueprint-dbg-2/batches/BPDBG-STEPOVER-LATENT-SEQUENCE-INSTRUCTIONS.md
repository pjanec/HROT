# BPDBG-STEPOVER-LATENT-SEQUENCE: Step Over picks wrong next node across a latent at the end of a Sequence branch

**Type:** Debugger-runtime navigation fix — HIGH blast radius (virtual-pointer stepping + tick-bridge). SONNET.
**Onboarding (read in order):** `.dev/.guides/DEV-GUIDE_claude.md`; `docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md`; this file. You MAY use the codebase-memory MCP for exploration first.
**DO NOT COMMIT.** Report to `.dev/blueprint-dbg-2/reports/BPDBG-STEPOVER-LATENT-SEQUENCE-REPORT.md`. The lead reviews + commits.

## Repro (user-confirmed, reproducible)
A blueprint with a top `Sequence` S0 having two branches:
- **Then0:** `SetVarA → Delay` (the Delay is the LAST node of Then0)
- **Then1:** `SetVarB → (cascaded Sequence S1 …) → Delay` (last node of Then1)

Stepping (Step Over):
- Within Then0: correctly pauses on `SetVarA`, then on the Then0 `Delay`. ✅
- **BUG:** Step Over from the Then0 `Delay` lands on the **last `Delay` of Then1**, skipping `SetVarB` AND the cascaded Sequence S1. ❌
- Expected (confirmed with user): land on the **first node actually executed next = first node of Then1 (`SetVarB`)**, then step through Then1 in execution order. (No synthetic stop on the Sequence node — it is not re-entered.)
- NOT a probe/coverage problem: a breakpoint set directly on the cascaded Sequence S1 (or any skipped node) DOES pause there. The defect is purely **next-node selection** in Step Over.

## Root cause (already diagnosed — verify, then fix)
`StepOver` → `StepForwardOrCF6` (`BlueprintDebugSession.cs:913`) → when at the last recorded node with a breakpoint armed → `StepFromNodeOrNextIteration` (`:982`). That method chooses the next pause target from **`ExecSuccessors.GetSuccessors` on the AUTHORED graph**:
- The Then0 `Delay`'s exec-out isn't wired onward in the authored graph (a `Sequence` drives Then0→Then1 *internally*), so it's classed `allTerminal` → **path (b)** (`:1009-1033`): assumes "end of tick → graph restart", sets a temp-BP on the `EventEntryNode`'s successors, and resumes.
- The true continuation is S0 advancing to **Then1**, not a graph restart. The temp-BP (on S0 / entry successor) never fires (S0 isn't re-entered mid-flight), so the resumed tick runs Then1 to its next **latent suspend** (last Delay of Then1) and the pointer ends up there → the intermediate synchronous nodes are skipped.

**The flaw:** next-node selection guesses from static authored-graph topology, which cannot model Sequence branch ordering or latent resume successors. The actual next-executed node is known only at runtime (the recorder captures the real execution order, entity-scoped).

## Fix (prescribed direction — implement + verify; adjust only if you prove it can't work and say why)
For the **RecordingActive tick-bridge** (the at-last-recorded-node step-past-end path), STOP deriving a temp-BP target from `ExecSuccessors`. Instead:
1. Add a one-shot **"pause on the next recorded node after a step-resume"** mode (e.g. a `_stepResumePending` flag) set when Step Over/Into is pressed at the last recorded node with recording active.
2. Resume the simulation (`RequestResume()` — the existing multi-tick resume; do NOT single-tick-advance, latents may span ticks). Let the recorder `BeginTick` the resumed tick and record the debugged entity's nodes in real execution order.
3. On the **first `RecordNodeEntry` / `OnNodeEnter` of the resumed tick** while `_stepResumePending` is set: re-pause, set `_nodePointer = 0` (the first recorded node of the new tick), clear the flag, restore scratch to the pointer, fire state-changed. Do NOT require/await the user breakpoint.

This unifies and SUBSUMES BF-03 and BF-04 (both become "land on the first node recorded in the resumed continuation"):
- BF-03 (Delay→synchronous successor): resumed continuation records that successor first → lands on it. ✓
- BF-04 (Delay→Return = end-of-graph-tick): the resumed tick IS the next iteration; its first recorded node is the first node of the next iteration → lands on it. ✓
- The Sequence case: resumed continuation records `SetVarB` (first node of Then1) first → lands on it. ✓ (bug fixed)

Keep the **no-recording CF-6 fallback** (`Step(fallbackStepMode)` / `LegacyStepOneTick`) unchanged for the no-recordings case. Recording is entity-scoped already (NGS-2.0-CT0), so "first recorded node" is the debugged entity's next node — confirm this holds.

**Edge cases:** resumed tick produces NO recording for the entity (blueprint finished / entity died) → degrade gracefully (stay paused / clamp at last node + clear pending flag; never dead-stall the clock). Arbitrary nesting depth and multiple latents are handled automatically by the recorder-order approach.

## Tests (the gate — behavioral, REAL pipeline; build assets in code, NEVER load scratch .bp.json)
Build a programmatic asset mirroring the repro: `EventEntry → Sequence S0 { Then0: SetVarA → Delay ; Then1: SetVarB → Sequence S1 { child… } → Delay }`. Use `BlueprintAssetBuilder` if it supports multi-branch Sequence + nested Sequence; otherwise build the `Graph` manually (see `PerNodeProbesTests.DataNodes_AreNotInBreakpointTargets` for the manual-graph pattern).
1. **Primary (the bug):** arm a breakpoint, run to pause; Step Over through Then0 (assert `CurrentNodeId` == SetVarA, then == Then0 Delay). Then Step Over from the Then0 Delay → assert `CurrentNodeId == SetVarB` (first node of Then1) — **NOT** the last Delay of Then1. Then Step Over again → the next Then1 node in execution order (proving no skip of the cascaded sequence's children).
2. **BF-03 regression (re-assert exact landing):** step past a `Delay` whose continuation is a synchronous node → lands on that node (post-Delay), not skipped.
3. **BF-04 regression (re-assert exact landing):** step past end-of-graph-tick (`Delay→Return`) → lands on the first node of the next iteration.
4. All existing `CF6_SteppingTests`, `VirtualPointerTests`, `TickBridgeTests`, `PerNodeProbesTests`, `SubTickRecorderIntegrationTests`, `NodeGranularEditorUITests` stay green.

## Do-not-stop-until-green (NO regen flags)
`dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` + `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`. Only acceptable failures = documented pre-existing reds: `AiPrimitive_EmitMatchesGoldenSource`(×2), `Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_GeneratedSource_Snapshot`, `WhenNode_ZeroAllocOnHotPath`. Flaky `AlcUnloadTests.Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` and transient `MapKeyboardKey.idl` build error → re-run once. If a compiler/source-gen change "doesn't take": `dotnet build <consumer>.csproj --no-incremental`.

## Constraints
- Touch only the debugger stepping logic in `BlueprintDebugSession.cs` (`StepForwardOrCF6`, `StepFromNodeOrNextIteration`, `OnNodeEnter`/`HandleBreakpointHit`, plus a one-shot pending-step flag) and test files. Do NOT change the recorder's capture/restore, the compiler, or unrelated runtime logic. Do NOT weaken existing tests. Do NOT load or commit any `.bp.json`. Do NOT commit.
- Narrow stop-condition: only if you prove the "pause on first recorded node of the resumed tick" mechanism cannot re-pause (e.g. no probe ever fires for the entity on resume) — document precisely and stop; do not redesign the recorder.
- Report: the verified root cause; the exact mechanism change; how BF-03/BF-04 are preserved (with the re-asserted landing nodes); the new test(s); full Passed/Failed/Skipped counts for both suites.
